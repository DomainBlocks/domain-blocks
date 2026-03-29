using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed partial class EventAppender(
    IMongoCollection<BsonDocument> eventLog,
    long epoch,
    long? initialCommitPosition,
    ILogger<EventAppender> logger) : IEventAppender
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

    private Task _prefetchTask = Task.CompletedTask;
    private long _nextPosition = initialCommitPosition.HasValue ? initialCommitPosition.Value + 1 : 0;

    public void StartPrefetch(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken)
    {
        ClearBuffers();
        _requests.AddRange(requests);
        _prefetchTask = _requests.Count == 0 ? Task.CompletedTask : PrefetchAsync(cancellationToken);
    }

    public async Task<WriteResult> FlushAsync(CancellationToken cancellationToken)
    {
        if (_requests.Count == 0)
            return new WriteResult(_nextPosition, _nextPosition);

        await _prefetchTask.ConfigureAwait(false);

        var nextPosition = BuildWriteModels();

        if (_writeModels.Count == 0)
        {
            logger.LogDebug("Batch produced no write models; skipping");
            return new WriteResult(_nextPosition, _nextPosition);
        }

        await _eventLog.BulkWriteAsync(_writeModels, OrderedBulkWriteOptions, cancellationToken).ConfigureAwait(false);

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

        return new WriteResult(startPosition, nextPosition);
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

            var expectedStreamState = ToExpectedStreamState(bsonExpectedStreamState);

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
            var @event = CreateBatchCompletedEvent(_appendedCommitIds, _duplicateCommitIds, _commitRejections.Values);

            var writeModel = CreateEventWrite(
                nextPosition++,
                epoch,
                BsonString.Empty,
                0,
                BsonEmptyGuid,
                @event);

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

    private static ExpectedStreamState ToExpectedStreamState(BsonValue bsonValue)
    {
        var doc = bsonValue.AsBsonDocument;

        return doc["kind"].AsString switch
        {
            "any" => ExpectedStreamState.Any,
            "streamExists" => ExpectedStreamState.StreamExists,
            "streamDoesNotExist" => ExpectedStreamState.StreamDoesNotExist,
            "version" => ExpectedStreamState.SpecificVersion(StreamVersion.FromInt64(doc["version"].AsInt64)),
            var k => throw new InvalidOperationException($"Unknown expected stream state kind: '{k}'")
        };
    }

    private static UpdateOneModel<BsonDocument> CreateEventWrite(
        long position,
        long epoch,
        BsonValue streamId,
        long streamVersion,
        BsonValue commitId,
        BsonDocument @event)
    {
        var filter = new BsonDocument
        {
            { "_id", position },
            { EventLogEntry.FieldNames.Epoch, new BsonDocument("$lt", epoch) }
        };

        var set = new BsonDocument
        {
            { "_id", position },
            { EventLogEntry.FieldNames.Epoch, epoch },
            { EventLogEntry.FieldNames.StreamId, streamId },
            { EventLogEntry.FieldNames.StreamVersion, streamVersion },
            { EventLogEntry.FieldNames.CommitId, commitId },
            { EventLogEntry.FieldNames.EventName, @event[PendingEvent.FieldNames.EventName] },
            { EventLogEntry.FieldNames.EventData, @event[PendingEvent.FieldNames.EventData] },
            { EventLogEntry.FieldNames.Metadata, @event[PendingEvent.FieldNames.Metadata] }
        };

        var update = new BsonDocument
        {
            { "$set", set },
            { "$currentDate", new BsonDocument(EventLogEntry.FieldNames.WrittenAtUtc, true) }
        };

        return new UpdateOneModel<BsonDocument>(filter, update) { IsUpsert = true };
    }

    private static BsonDocument CreateCommitRejection(
        Guid commitId,
        BsonValue streamId,
        BsonValue expectedStreamState,
        StreamState actualStreamState)
    {
        var actualStreamStateDoc = new BsonDocument();

        if (actualStreamState.IsStreamDoesNotExist)
        {
            actualStreamStateDoc["kind"] = "streamDoesNotExist";
        }
        else if (actualStreamState.IsStreamExists)
        {
            actualStreamStateDoc["kind"] = "streamExists";
            actualStreamStateDoc["version"] = checked((long)actualStreamState.Version.Value.Value);
        }
        else
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualStreamState), $"Unknown {nameof(StreamStateKind)}: {actualStreamState.Kind}");
        }

        return new BsonDocument
        {
            { CommitRejection.FieldNames.CommitId, new BsonBinaryData(commitId, GuidRepresentation.Standard) },
            { CommitRejection.FieldNames.StreamId, streamId },
            { CommitRejection.FieldNames.ExpectedStreamState, expectedStreamState },
            { CommitRejection.FieldNames.ActualStreamState, actualStreamStateDoc }
        };
    }

    private static BsonDocument CreateBatchCompletedEvent(
        IEnumerable<Guid> appendedCommitIds,
        IEnumerable<Guid> duplicateCommitIds,
        IEnumerable<BsonValue> commitRejections)
    {
        var eventPayload = new BsonDocument
        {
            {
                AppendBatchCompleted.FieldNames.AppendedCommitIds,
                new BsonArray(appendedCommitIds.Select(x => new BsonBinaryData(x, GuidRepresentation.Standard)))
            },
            {
                AppendBatchCompleted.FieldNames.DuplicateCommitIds,
                new BsonArray(duplicateCommitIds.Select(x => new BsonBinaryData(x, GuidRepresentation.Standard)))
            },
            { AppendBatchCompleted.FieldNames.Rejections, new BsonArray(commitRejections) }
        };

        return new BsonDocument
        {
            { PendingEvent.FieldNames.EventName, "AppendBatchCompleted" },
            { PendingEvent.FieldNames.EventData, eventPayload },
            { PendingEvent.FieldNames.Metadata, BsonNull.Value }
        };
    }
}