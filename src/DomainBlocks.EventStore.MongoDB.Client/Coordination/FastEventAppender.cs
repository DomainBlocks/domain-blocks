using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class FastEventAppender(
    IMongoCollection<BsonDocument> eventLog,
    long epoch,
    long? initialCommitPosition,
    ILogger<FastEventAppender> logger) : IFastEventAppender
{
    private static readonly BulkWriteOptions OrderedBulkWriteOptions = new() { IsOrdered = true };
    private static readonly BsonBinaryData BsonEmptyGuid = new(Guid.Empty, GuidRepresentation.Standard);

    private readonly IMongoCollection<BsonDocument> _eventLog = eventLog
        .WithReadConcern(ReadConcern.Majority)
        .WithReadPreference(ReadPreference.Primary)
        .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    private long _nextPosition = initialCommitPosition.HasValue ? initialCommitPosition.Value + 1 : 0;

    private readonly PrefetchQuery _prefetchQuery = new(epoch, initialCommitPosition);
    private Task _prefetchTask = Task.CompletedTask;

    private readonly NonConcurrentPool<CommitRejectionSlot> _commitRejectionPool =
        NonConcurrentPool.Create<CommitRejectionSlot>();

    private readonly NonConcurrentPool<EventWriteSlot> _eventWritePool =
        NonConcurrentPool.Create(() => new EventWriteSlot(epoch));

    private readonly AppendBatchCompletedEventSlot _completedEventSlot = new();

    private readonly List<BsonDocument> _requests = [];
    private readonly HashSet<Guid> _appendedCommitIds = [];
    private readonly HashSet<Guid> _duplicateCommitIds = [];
    private readonly Dictionary<Guid, BsonDocument> _commitRejections = [];
    private readonly Dictionary<string, long> _streamVersions = [];
    private readonly List<WriteModel<BsonDocument>> _writeModels = [];

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

        await _eventLog
            .BulkWriteAsync(_writeModels, OrderedBulkWriteOptions, cancellationToken)
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

        return new WriteResult(startPosition, nextPosition);
    }

    private void ClearBuffers()
    {
        _requests.Clear();
        _appendedCommitIds.Clear();
        _duplicateCommitIds.Clear();
        _commitRejections.Clear();
        _streamVersions.Clear();
        _writeModels.Clear();

        _commitRejectionPool.ReturnAll();
        _eventWritePool.ReturnAll();
    }

    private async Task PrefetchAsync(CancellationToken ct)
    {
        var result = await _prefetchQuery.ExecuteAsync(_eventLog, _requests, ct).ConfigureAwait(false);

        foreach (var value in result.ExistingCommitIds)
            _duplicateCommitIds.Add(value.AsGuid);

        foreach (var doc in result.HeadStreamVersions)
            _streamVersions.Add(doc["_id"].AsString, doc["version"].AsInt64);

        logger.LogDebug(
            "Prefetch complete: {DuplicateCount} duplicate(s) found, {StreamCount} stream version(s) loaded",
            _duplicateCommitIds.Count,
            _streamVersions.Count);
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
            var streamVersion = _streamVersions.GetValueOrDefault(streamId, -1);

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

                var rejection = _commitRejectionPool.RentOne().Fill(
                    commitId,
                    bsonStreamId,
                    bsonExpectedStreamState,
                    actualStreamState);

                _commitRejections.Add(commitId, rejection);

                continue;
            }

            var eventWriteSlots = _eventWritePool.RentMany(events.Count);

            for (var i = 0; i < events.Count; i++)
            {
                var writeModel = eventWriteSlots[i].Fill(
                    nextPosition++,
                    bsonStreamId,
                    ++streamVersion,
                    bsonCommitId,
                    events[i].AsBsonDocument);

                _writeModels.Add(writeModel);
            }

            _streamVersions[streamId] = streamVersion;
            _appendedCommitIds.Add(commitId);
        }

        if (HasCommits())
        {
            var @event = _completedEventSlot.Fill(
                _appendedCommitIds,
                _duplicateCommitIds,
                _commitRejections.Values);

            var writeModel = _eventWritePool.RentOne().Fill(
                nextPosition++,
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
}