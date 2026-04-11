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

    // Reused buffers
    private readonly List<BsonDocument> _requests = [];
    private readonly HashSet<Guid> _appendedCommitIds = [];
    private readonly HashSet<Guid> _duplicateCommitIds = [];
    private readonly Dictionary<Guid, BsonDocument> _conflicts = [];
    private readonly Dictionary<string, long> _headStreamVersions = [];
    private readonly List<ReplaceOneModel<BsonDocument>> _writes = [];

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

        ClearBuffers();

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
                return EventLogWriteResult.Empty;

            await _prepareTask.ConfigureAwait(false);

            var (duplicatesSkipped, conflictsRejected) = CreateWrites();

            if (_writes.Count == 0)
            {
                logger.LogDebug("Batch produced no writes; skipping");
                return EventLogWriteResult.Empty;
            }

            await eventLog.BulkWriteAsync(_writes, OrderedBulkWriteOptions, cancellationToken).ConfigureAwait(false);

            var startPosition = _nextPosition;
            _nextPosition += _writes.Count;

            logger.LogInformation(
                "Batch appended: positions {StartPosition}–{EndPosition}, " +
                "{AppendCount} appended, {DuplicateCount} duplicate(s), {RejectionCount} rejected",
                startPosition,
                _nextPosition - 1,
                _appendedCommitIds.Count,
                _duplicateCommitIds.Count,
                _conflicts.Count);

            return new EventLogWriteResult(startPosition, _writes.Count, duplicatesSkipped, conflictsRejected);
        }
        finally
        {
            _isPrepared = false;
        }
    }

    private void ClearBuffers()
    {
        _requests.Clear();
        _appendedCommitIds.Clear();
        _duplicateCommitIds.Clear();
        _conflicts.Clear();
        _headStreamVersions.Clear();
        _writes.Clear();
    }

    private (BsonDocument? DuplicatesSkipped, BsonDocument? ConflictsRejected) CreateWrites()
    {
        BsonDocument? duplicatesSkippedData = null;
        BsonDocument? conflictsRejectedData = null;
        var writtenAtUtc = DateTime.UtcNow;

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

                var conflict = CreateConflict(
                    commitId,
                    bsonStreamId,
                    bsonExpectedStreamState,
                    BsonDocument.From(actualStreamState));

                _conflicts.Add(commitId, conflict);

                continue;
            }

            var commitIndex = 0;
            foreach (var e in events)
            {
                var replaceOneModel = CreateReplaceOneModel(
                    _nextPosition + _writes.Count,
                    epoch,
                    bsonStreamId,
                    ++streamVersion,
                    bsonCommitId,
                    commitIndex++,
                    e.AsBsonDocument,
                    writtenAtUtc);

                _writes.Add(replaceOneModel);
            }

            _headStreamVersions[streamId] = streamVersion;
            _appendedCommitIds.Add(commitId);
        }

        if (_duplicateCommitIds.Count > 0)
        {
            duplicatesSkippedData = CreateDuplicatesSkippedData();

            var replaceOneModel = CreateReplaceOneModel(
                _nextPosition + _writes.Count,
                epoch,
                BsonString.Empty,
                0,
                BsonNull.Value,
                0,
                nameof(EventNames.DuplicatesSkipped),
                duplicatesSkippedData,
                BsonNull.Value,
                writtenAtUtc);

            _writes.Add(replaceOneModel);
        }

        if (_conflicts.Count > 0)
        {
            conflictsRejectedData = CreateConflictsRejectedData();

            var replaceOneModel = CreateReplaceOneModel(
                _nextPosition + _writes.Count,
                epoch,
                streamId: BsonString.Empty,
                streamVersion: 0,
                commitId: BsonNull.Value,
                commitIndex: 0,
                nameof(EventNames.ConflictsRejected),
                conflictsRejectedData,
                metadata: BsonNull.Value,
                writtenAtUtc);

            _writes.Add(replaceOneModel);
        }

        return (duplicatesSkippedData, conflictsRejectedData);

        bool IsProcessed(Guid id) =>
            _appendedCommitIds.Contains(id) ||
            _duplicateCommitIds.Contains(id) ||
            _conflicts.ContainsKey(id);
    }

    private static ReplaceOneModel<BsonDocument> CreateReplaceOneModel(
        long position,
        long epoch,
        BsonValue streamId,
        long streamVersion,
        BsonValue commitId,
        int commitIndex,
        BsonDocument pendingEvent,
        DateTime writtenAtUtc)
    {
        return CreateReplaceOneModel(
            position,
            epoch,
            streamId,
            streamVersion,
            commitId,
            commitIndex,
            pendingEvent[PendingEvent.FieldNames.EventName],
            pendingEvent[PendingEvent.FieldNames.EventData],
            pendingEvent[PendingEvent.FieldNames.Metadata],
            writtenAtUtc);
    }

    private static ReplaceOneModel<BsonDocument> CreateReplaceOneModel(
        long position,
        long epoch,
        BsonValue streamId,
        long streamVersion,
        BsonValue commitId,
        int commitIndex,
        BsonValue eventName,
        BsonValue eventData,
        BsonValue metadata,
        DateTime writtenAtUtc)
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
            { EventLogEntry.FieldNames.CommitIndex, commitIndex },
            { EventLogEntry.FieldNames.EventName, eventName },
            { EventLogEntry.FieldNames.EventData, eventData },
            { EventLogEntry.FieldNames.Metadata, metadata },
            { EventLogEntry.FieldNames.WrittenAtUtc, writtenAtUtc }
        };

        return new ReplaceOneModel<BsonDocument>(filter, replacement) { IsUpsert = true };
    }

    private static BsonDocument CreateConflict(
        Guid commitId,
        BsonValue streamId,
        BsonValue expectedStreamState,
        BsonValue actualStreamState)
    {
        return new BsonDocument
        {
            { AppendConflict.FieldNames.CommitId, new BsonBinaryData(commitId, GuidRepresentation.Standard) },
            { AppendConflict.FieldNames.StreamId, streamId },
            { AppendConflict.FieldNames.ExpectedStreamState, expectedStreamState },
            { AppendConflict.FieldNames.ActualStreamState, actualStreamState }
        };
    }

    private BsonDocument CreateDuplicatesSkippedData()
    {
        return new BsonDocument
        {
            {
                DuplicatesSkipped.FieldNames.CommitIds,
                new BsonArray(_duplicateCommitIds.Select(x => new BsonBinaryData(x, GuidRepresentation.Standard)))
            }
        };
    }

    private BsonDocument CreateConflictsRejectedData()
    {
        return new BsonDocument
        {
            {
                ConflictsRejected.FieldNames.Conflicts,
                new BsonArray(_conflicts.Values)
            }
        };
    }
}