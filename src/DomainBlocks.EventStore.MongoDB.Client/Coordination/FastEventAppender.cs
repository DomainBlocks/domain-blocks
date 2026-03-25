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
    ILogger<FastEventAppender> logger) :
    IFastEventAppender
{
    private static readonly BsonDocument GroupByStreamStage = new("$group", new BsonDocument
    {
        { "_id", $"${EventLogEntry.FieldNames.StreamId}" },
        { "maxVersion", new BsonDocument("$max", $"${EventLogEntry.FieldNames.StreamVersion}") }
    });

    private readonly IMongoCollection<BsonDocument> _eventLog = eventLog
        .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    private long _nextPosition = initialCommitPosition.HasValue
        ? initialCommitPosition.Value + 1
        : 0;

    private readonly BsonDocument _visibilityFilter = CreateVisibilityFilter(epoch, initialCommitPosition);

    private Task _prefetchTask = Task.CompletedTask;

    // Reusable per-batch buffers - cleared each batch, never reallocated.
    private readonly List<BsonDocument> _requests = [];
    private readonly HashSet<Guid> _appendedCommitIds = [];
    private readonly HashSet<Guid> _duplicateCommitIds = [];
    private readonly Dictionary<Guid, BsonDocument> _rejections = [];
    private readonly Dictionary<string, long> _streamVersions = [];
    private readonly List<WriteModel<BsonDocument>> _writeModels = [];

    public void StartPrefetch(IEnumerable<BsonDocument> requests, CancellationToken ct)
    {
        ClearBuffers();
        _requests.AddRange(requests);
        _prefetchTask = _requests.Count == 0 ? Task.CompletedTask : PrefetchAsync(ct);
    }

    public async Task<AppendBatchResult> FlushAsync(CancellationToken ct)
    {
        if (_requests.Count == 0)
            return new AppendBatchResult(_nextPosition, _nextPosition);

        await _prefetchTask.ConfigureAwait(false);

        var nextPosition = BuildWriteModels();

        if (_writeModels.Count == 0)
        {
            logger.LogDebug("Batch produced no write models; skipping");
            return new AppendBatchResult(_nextPosition, _nextPosition);
        }

        await _eventLog
            .BulkWriteAsync(_writeModels, new BulkWriteOptions { IsOrdered = true }, ct)
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
            _rejections.Count);

        return new AppendBatchResult(startPosition, nextPosition);
    }

    // public async Task<AppendBatchResult> AppendBatchAsync(
    //     IEnumerable<BsonDocument> requests,
    //     CancellationToken cancellationToken = default)
    // {
    //     ClearBuffers();
    //
    //     _requests.AddRange(requests);
    //     if (_requests.Count == 0)
    //         return new AppendBatchResult(_nextPosition, _nextPosition);
    //
    //     logger.LogDebug("Appending batch of {RequestCount} request(s) at epoch {Epoch}", _requests.Count, epoch);
    //
    //     await PrefetchAsync(cancellationToken).ConfigureAwait(false);
    //
    //     var nextPosition = BuildWriteModels();
    //
    //     if (_writeModels.Count == 0)
    //     {
    //         logger.LogDebug("Batch produced no write models; skipping");
    //         return new AppendBatchResult(_nextPosition, _nextPosition);
    //     }
    //
    //     await _eventLog
    //         .BulkWriteAsync(_writeModels, new BulkWriteOptions { IsOrdered = true }, cancellationToken)
    //         .ConfigureAwait(false);
    //
    //     // Only advance after a successful write.
    //     var startPosition = _nextPosition;
    //     _nextPosition = nextPosition;
    //
    //     logger.LogInformation(
    //         "Batch appended: positions {StartPosition}–{EndPosition}, " +
    //         "{AppendCount} appended, {DuplicateCount} duplicate(s), {RejectionCount} rejected, ",
    //         startPosition,
    //         nextPosition - 1,
    //         _appendedCommitIds.Count,
    //         _duplicateCommitIds.Count,
    //         _rejections.Count);
    //
    //     return new AppendBatchResult(startPosition, nextPosition);
    // }

    private static BsonDocument CreateVisibilityFilter(long epoch, long? initialCommitPosition)
    {
        return initialCommitPosition.HasValue
            ? new BsonDocument("$or", new BsonArray
            {
                new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch),
                new BsonDocument("_id", new BsonDocument("$lte", initialCommitPosition.Value))
            })
            : new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch);
    }

    private void ClearBuffers()
    {
        _requests.Clear();
        _appendedCommitIds.Clear();
        _duplicateCommitIds.Clear();
        _rejections.Clear();
        _streamVersions.Clear();
        _writeModels.Clear();
    }

    private async Task PrefetchAsync(CancellationToken ct)
    {
        var allCommitIds = _requests.Select(r => r[AppendRequest.FieldNames.CommitId]).Distinct();
        var allStreamIds = _requests.Select(r => r[AppendRequest.FieldNames.StreamId]).Distinct();

        var commitIdsArray = new BsonArray(allCommitIds);
        var streamIdsArray = new BsonArray(allStreamIds);

        var commitMatchFilter = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument(EventLogEntry.FieldNames.CommitId, new BsonDocument("$in", commitIdsArray)),
            _visibilityFilter
        });

        var streamMatchStage = new BsonDocument("$match", new BsonDocument("$and", new BsonArray
        {
            new BsonDocument(EventLogEntry.FieldNames.StreamId, new BsonDocument("$in", streamIdsArray)),
            _visibilityFilter
        }));

        var duplicatesTask = _eventLog
            .Distinct<BsonValue>(
                EventLogEntry.FieldNames.CommitId,
                commitMatchFilter,
                cancellationToken: ct)
            .ToListAsync(ct);

        var versionsTask = _eventLog
            .Aggregate<BsonDocument>(new[] { streamMatchStage, GroupByStreamStage })
            .ToListAsync(ct);

        await Task.WhenAll(duplicatesTask, versionsTask).ConfigureAwait(false);

        foreach (var value in await duplicatesTask.ConfigureAwait(false))
            _duplicateCommitIds.Add(value.AsGuid);

        foreach (var doc in await versionsTask.ConfigureAwait(false))
            _streamVersions.Add(doc["_id"].AsString, doc["maxVersion"].AsInt64);

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

            var commitId = request[AppendRequest.FieldNames.CommitId].AsGuid;
            if (IsProcessed(commitId))
                continue;

            var streamId = request[AppendRequest.FieldNames.StreamId].AsString;
            var streamVersion = _streamVersions.GetValueOrDefault(streamId, -1);

            // OCC check: convert the raw long to a StreamState and use the existing Matches logic.
            var actualStreamState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            var expectedStreamState = ToExpectedStreamState(request[AppendRequest.FieldNames.ExpectedStreamState]);

            if (!expectedStreamState.Matches(actualStreamState))
            {
                logger.LogWarning(
                    "Append rejected for commit ID {CommitId}, stream '{StreamId}': " +
                    "expected {ExpectedState}, actual {ActualState}",
                    commitId,
                    streamId,
                    expectedStreamState,
                    actualStreamState);

                var rejection = new BsonDocument
                {
                    { CommitRejection.FieldNames.CommitId, new BsonBinaryData(commitId, GuidRepresentation.Standard) },
                    { CommitRejection.FieldNames.StreamId, streamId },
                    {
                        CommitRejection.FieldNames.ExpectedStreamState,
                        request[AppendRequest.FieldNames.ExpectedStreamState]
                    },
                    { CommitRejection.FieldNames.ActualStreamState, SerializeStreamState(actualStreamState) }
                };

                _rejections.Add(commitId, rejection);

                continue;
            }

            foreach (var e in events)
                _writeModels.Add(CreateEventWrite(nextPosition++, request, ++streamVersion, e.AsBsonDocument));

            _streamVersions[streamId] = streamVersion;
            _appendedCommitIds.Add(commitId);
        }

        if (HasCommits())
            _writeModels.Add(CreateBatchCompletedWrite(nextPosition++));

        return nextPosition;

        bool IsProcessed(Guid commitId) =>
            _appendedCommitIds.Contains(commitId) ||
            _duplicateCommitIds.Contains(commitId) ||
            _rejections.ContainsKey(commitId);

        bool HasCommits() =>
            _appendedCommitIds.Count > 0 ||
            _duplicateCommitIds.Count > 0 ||
            _rejections.Count > 0;
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

    private static BsonDocument SerializeStreamState(StreamState value)
    {
        return value.Kind switch
        {
            StreamStateKind.StreamDoesNotExist => new BsonDocument("kind", "streamDoesNotExist"),

            StreamStateKind.StreamExists => new BsonDocument
            {
                { "kind", "streamExists" },
                { "version", checked((long)value.Version!.Value.Value) }
            },

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Unknown {nameof(StreamStateKind)}: {value.Kind}")
        };
    }

    private WriteModel<BsonDocument> CreateEventWrite(
        long position,
        BsonDocument request,
        long streamVersion,
        BsonDocument @event)
    {
        var filter = new BsonDocument
        {
            { "_id", position },
            { EventLogEntry.FieldNames.Epoch, new BsonDocument("$lt", epoch) }
        };

        var update = new BsonDocument
        {
            {
                "$set", new BsonDocument
                {
                    { "_id", position },
                    { EventLogEntry.FieldNames.Epoch, epoch },
                    { EventLogEntry.FieldNames.StreamId, request[AppendRequest.FieldNames.StreamId] },
                    { EventLogEntry.FieldNames.StreamVersion, streamVersion },
                    { EventLogEntry.FieldNames.CommitId, request[AppendRequest.FieldNames.CommitId] },
                    { EventLogEntry.FieldNames.EventName, @event[PendingEvent.FieldNames.EventName] },
                    { EventLogEntry.FieldNames.EventData, @event[PendingEvent.FieldNames.EventData] },
                    { EventLogEntry.FieldNames.Metadata, @event[PendingEvent.FieldNames.Metadata] }
                }
            },
            { "$currentDate", new BsonDocument(EventLogEntry.FieldNames.WrittenAtUtc, true) }
        };

        return new UpdateOneModel<BsonDocument>(filter, update) { IsUpsert = true };
    }

    private WriteModel<BsonDocument> CreateBatchCompletedWrite(long position)
    {
        var filter = new BsonDocument
        {
            { "_id", position },
            { EventLogEntry.FieldNames.Epoch, new BsonDocument("$lt", epoch) }
        };

        var appendsArray = new BsonArray(
            _appendedCommitIds.Select(id => new BsonBinaryData(id, GuidRepresentation.Standard)));

        var duplicatesArray = new BsonArray(
            _duplicateCommitIds.Select(id => new BsonBinaryData(id, GuidRepresentation.Standard)));

        var rejectionsArray = new BsonArray(_rejections.Values);

        var batchCompleted = new BsonDocument
        {
            { AppendBatchCompleted.FieldNames.AppendedCommitIds, appendsArray },
            { AppendBatchCompleted.FieldNames.DuplicateCommitIds, duplicatesArray },
            { AppendBatchCompleted.FieldNames.Rejections, rejectionsArray }
        };

        var update = new BsonDocument
        {
            {
                "$set", new BsonDocument
                {
                    { "_id", position },
                    { EventLogEntry.FieldNames.Epoch, epoch },
                    { EventLogEntry.FieldNames.StreamId, string.Empty },
                    { EventLogEntry.FieldNames.StreamVersion, 0L },
                    { EventLogEntry.FieldNames.CommitId, new BsonBinaryData(Guid.Empty, GuidRepresentation.Standard) },
                    { EventLogEntry.FieldNames.EventName, "AppendBatchCompleted" },
                    { EventLogEntry.FieldNames.EventData, batchCompleted },
                    { EventLogEntry.FieldNames.Metadata, BsonNull.Value },
                }
            },
            { "$currentDate", new BsonDocument(EventLogEntry.FieldNames.WrittenAtUtc, true) }
        };

        return new UpdateOneModel<BsonDocument>(filter, update) { IsUpsert = true };
    }
}