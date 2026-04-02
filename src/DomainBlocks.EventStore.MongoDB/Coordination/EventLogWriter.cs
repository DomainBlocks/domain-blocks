using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Serialization;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed partial class EventLogWriter(
    IMongoCollection<BsonDocument> eventLog,
    long epoch,
    long? epochStartPosition,
    ILogger<EventLogWriter> logger) :
    IEventLogWriter
{
    private static readonly BulkWriteOptions OrderedBulkWriteOptions = new() { IsOrdered = true };
    private static readonly BsonBinaryData BsonEmptyGuid = new(Guid.Empty, GuidRepresentation.Standard);

    private readonly IMongoCollection<BsonDocument> _eventLog = eventLog
        .WithReadConcern(ReadConcern.Majority)
        .WithReadPreference(ReadPreference.Primary)
        .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    // Reused buffers
    private readonly List<BsonDocument> _requests = [];
    private readonly HashSet<Guid> _appendedCommitIds = [];
    private readonly HashSet<Guid> _duplicateCommitIds = [];
    private readonly Dictionary<Guid, BsonDocument> _commitRejections = [];
    private readonly Dictionary<string, long> _headStreamVersions = [];
    private readonly List<WriteModel<BsonDocument>> _writeModels = [];

    private bool _isPrepared;
    private Task _prepareTask = Task.CompletedTask;
    private long _nextPosition = epochStartPosition.HasValue ? epochStartPosition.Value + 1 : 0;

    public void Prepare(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken)
    {
        if (_isPrepared)
        {
            throw new InvalidOperationException(
                "Prepare has already been called. Call FlushAsync before calling Prepare again.");
        }

        _requests.AddRange(requests);
        _prepareTask = _requests.Count == 0 ? Task.CompletedTask : PrepareAsync(cancellationToken);
        _isPrepared = true;
    }

    public async Task<EventLogWriteResult> FlushAsync(CancellationToken cancellationToken)
    {
        if (!_isPrepared)
            throw new InvalidOperationException("FlushAsync called without a preceding call to Prepare.");

        try
        {
            if (_requests.Count == 0)
                return new EventLogWriteResult(_nextPosition, _nextPosition);

            await _prepareTask.ConfigureAwait(false);

            var nextPosition = BuildWriteModels();

            if (_writeModels.Count == 0)
            {
                logger.LogDebug("Batch produced no write models; skipping");
                return new EventLogWriteResult(_nextPosition, _nextPosition);
            }

            await _eventLog.BulkWriteAsync(_writeModels, OrderedBulkWriteOptions, cancellationToken)
                .ConfigureAwait(false);

            var startPosition = _nextPosition;
            _nextPosition = nextPosition;

            logger.LogInformation(
                "Batch appended: positions {StartPosition}–{EndPosition}, " +
                "{AppendCount} appended, {DuplicateCount} duplicate(s), {RejectionCount} rejected",
                startPosition,
                nextPosition - 1,
                _appendedCommitIds.Count,
                _duplicateCommitIds.Count,
                _commitRejections.Count);

            return new EventLogWriteResult(startPosition, nextPosition);
        }
        finally
        {
            ClearBuffers();
            _isPrepared = false;
        }
    }

    private void ClearBuffers()
    {
        _requests.Clear();
        _appendedCommitIds.Clear();
        _duplicateCommitIds.Clear();
        _commitRejections.Clear();
        _headStreamVersions.Clear();
        _writeModels.Clear();
    }

    private long BuildWriteModels()
    {
        var nextPosition = _nextPosition;

        foreach (var request in _requests)
        {
            var events = request[AppendRequest.FieldNames.Events].AsBsonArray;
            if (events.Count == 0)
                continue;

            var bsonCommitId = request[AppendRequest.FieldNames.CommitId];
            var bsonStreamId = request[AppendRequest.FieldNames.StreamId];
            var bsonExpectedStreamState = request[AppendRequest.FieldNames.ExpectedStreamState];

            var commitId = bsonCommitId.AsGuid;
            if (IsProcessed(commitId))
                continue;

            var streamId = bsonStreamId.AsString;
            var streamVersion = _headStreamVersions.GetValueOrDefault(streamId, -1);

            var actualStreamState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            var expectedStreamState = bsonExpectedStreamState.ToExpectedStreamState();
            if (!expectedStreamState.Matches(actualStreamState))
            {
                logger.LogWarning(
                    "Append rejected for commit ID {CommitId}, stream '{StreamId}': " +
                    "expected {ExpectedState}, actual {ActualState}",
                    commitId,
                    streamId,
                    expectedStreamState,
                    actualStreamState);

                var rejection = CreateCommitRejection(
                    commitId,
                    bsonStreamId,
                    bsonExpectedStreamState,
                    actualStreamState);

                _commitRejections.Add(commitId, rejection);

                continue;
            }

            foreach (var @event in events)
            {
                var writeModel = CreateEventWrite(
                    nextPosition++,
                    epoch,
                    bsonStreamId,
                    ++streamVersion,
                    bsonCommitId,
                    @event.AsBsonDocument);

                _writeModels.Add(writeModel);
            }

            _headStreamVersions[streamId] = streamVersion;
            _appendedCommitIds.Add(commitId);
        }

        if (HasCommits())
        {
            var writeModel = CreateEventWrite(
                nextPosition++,
                epoch,
                BsonString.Empty,
                0,
                BsonEmptyGuid,
                nameof(EventNames.AppendBatchRecorded),
                CreateBatchRecordedEventData());

            _writeModels.Add(writeModel);
        }

        return nextPosition;

        bool IsProcessed(Guid id) =>
            _appendedCommitIds.Contains(id) ||
            _duplicateCommitIds.Contains(id) ||
            _commitRejections.ContainsKey(id);

        bool HasCommits() =>
            _appendedCommitIds.Count > 0 ||
            _duplicateCommitIds.Count > 0 ||
            _commitRejections.Count > 0;
    }

    private static ReplaceOneModel<BsonDocument> CreateEventWrite(
        long position,
        long epoch,
        BsonValue streamId,
        long streamVersion,
        BsonValue commitId,
        BsonDocument pendingEvent)
    {
        return CreateEventWrite(
            position,
            epoch,
            streamId,
            streamVersion,
            commitId,
            pendingEvent[PendingEvent.FieldNames.EventName],
            pendingEvent[PendingEvent.FieldNames.EventData],
            pendingEvent[PendingEvent.FieldNames.Metadata]);
    }

    private static ReplaceOneModel<BsonDocument> CreateEventWrite(
        long position,
        long epoch,
        BsonValue streamId,
        long streamVersion,
        BsonValue commitId,
        BsonValue eventName,
        BsonValue eventData,
        BsonValue? metadata = null)
    {
        var filter = new BsonDocument
        {
            { "_id", position },
            { EventLogEntry.FieldNames.Epoch, new BsonDocument("$lt", epoch) }
        };

        var replacement = new BsonDocument
        {
            { "_id", position },
            { EventLogEntry.FieldNames.Epoch, epoch },
            { EventLogEntry.FieldNames.StreamId, streamId },
            { EventLogEntry.FieldNames.StreamVersion, streamVersion },
            { EventLogEntry.FieldNames.CommitId, commitId },
            { EventLogEntry.FieldNames.EventName, eventName },
            { EventLogEntry.FieldNames.EventData, eventData },
            { EventLogEntry.FieldNames.Metadata, metadata ?? BsonNull.Value },
            { EventLogEntry.FieldNames.WrittenAtUtc, DateTime.UtcNow }
        };

        return new ReplaceOneModel<BsonDocument>(filter, replacement) { IsUpsert = true };
    }

    private static BsonDocument CreateCommitRejection(
        Guid commitId,
        BsonValue streamId,
        BsonValue expectedStreamState,
        StreamState actualStreamState)
    {
        return new BsonDocument
        {
            { CommitRejection.FieldNames.CommitId, new BsonBinaryData(commitId, GuidRepresentation.Standard) },
            { CommitRejection.FieldNames.StreamId, streamId },
            { CommitRejection.FieldNames.ExpectedStreamState, expectedStreamState },
            { CommitRejection.FieldNames.ActualStreamState, BsonDocument.From(actualStreamState) }
        };
    }

    private BsonDocument CreateBatchRecordedEventData()
    {
        return new BsonDocument
        {
            {
                AppendBatchRecorded.FieldNames.AppendedCommitIds,
                new BsonArray(_appendedCommitIds.Select(x => new BsonBinaryData(x, GuidRepresentation.Standard)))
            },
            {
                AppendBatchRecorded.FieldNames.DuplicateCommitIds,
                new BsonArray(_duplicateCommitIds.Select(x => new BsonBinaryData(x, GuidRepresentation.Standard)))
            },
            { AppendBatchRecorded.FieldNames.Rejections, new BsonArray(_commitRejections.Values) }
        };
    }
}