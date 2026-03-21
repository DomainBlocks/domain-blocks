using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppender(
    IMongoCollection<EventLogEntry> eventLog,
    long epoch,
    long? initialCommitPosition,
    ILogger<EventAppender> logger)
{
    private long _nextPosition = initialCommitPosition.HasValue
        ? initialCommitPosition.Value + 1
        : 0;

    private readonly FilterDefinition<EventLogEntry> _visibilityFilter =
        CreateVisibilityFilter(epoch, initialCommitPosition);

    // Reusable per-batch buffers - cleared each batch, never reallocated.
    private readonly List<AppendRequest> _requests = [];
    private readonly HashSet<Guid> _appendedCommitIds = [];
    private readonly HashSet<Guid> _duplicateCommitIds = [];
    private readonly Dictionary<Guid, CommitRejection> _rejections = [];
    private readonly Dictionary<string, long> _streamVersions = [];
    private readonly List<WriteModel<EventLogEntry>> _writeModels = [];

    public async Task AppendBatchAsync(
        IEnumerable<AppendRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ClearBuffers();

        _requests.AddRange(requests);
        if (_requests.Count == 0)
            return;

        logger.LogDebug("Appending batch of {RequestCount} request(s) at epoch {Epoch}", _requests.Count, epoch);

        await PrefetchAsync(cancellationToken).ConfigureAwait(false);

        var nextPosition = BuildWriteModels();

        if (_writeModels.Count == 0)
        {
            logger.LogDebug("Batch produced no write models; skipping");
            return;
        }

        await eventLog
            .BulkWriteAsync(_writeModels, new BulkWriteOptions { IsOrdered = true }, cancellationToken)
            .ConfigureAwait(false);

        // Only advance after a successful write.
        var startPosition = _nextPosition;
        _nextPosition = nextPosition;

        logger.LogInformation(
            "Batch appended: positions {StartPosition}–{EndPosition}, " +
            "{AppendCount} appended, {DuplicateCount} duplicate(s), {RejectionCount} rejected, ",
            startPosition,
            nextPosition - 1,
            _appendedCommitIds.Count,
            _duplicateCommitIds.Count,
            _rejections.Count);
    }

    private static FilterDefinition<EventLogEntry> CreateVisibilityFilter(long epoch, long? initialCommitPosition)
    {
        return initialCommitPosition.HasValue
            ? Builders<EventLogEntry>.Filter.Eq(x => x.Epoch, epoch) |
              Builders<EventLogEntry>.Filter.Lte(x => x.Position, initialCommitPosition.Value)
            : Builders<EventLogEntry>.Filter.Eq(x => x.Epoch, epoch);
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
        var allCommitIds = _requests.Select(r => r.CommitId).Distinct();
        var allStreamIds = _requests.Select(r => r.StreamId).Distinct();

        var duplicatesTask = eventLog
            .Distinct(
                x => x.CommitId,
                Builders<EventLogEntry>.Filter.In(x => x.CommitId, allCommitIds) & _visibilityFilter,
                cancellationToken: ct)
            .ToListAsync(ct);

        var versionsTask = eventLog
            .Aggregate()
            .Match(Builders<EventLogEntry>.Filter.In(x => x.StreamId, allStreamIds) & _visibilityFilter)
            .Group(x => x.StreamId, g => new { StreamId = g.Key, MaxVersion = g.Max(x => x.StreamVersion) })
            .ToListAsync(ct);

        await Task.WhenAll(duplicatesTask, versionsTask).ConfigureAwait(false);

        foreach (var id in await duplicatesTask.ConfigureAwait(false))
            _duplicateCommitIds.Add(id);

        foreach (var v in await versionsTask.ConfigureAwait(false))
            _streamVersions.Add(v.StreamId, v.MaxVersion);

        logger.LogDebug(
            "Prefetch complete: {DuplicateCount} duplicate(s) found, {StreamCount} stream version(s) loaded",
            _duplicateCommitIds.Count,
            _streamVersions.Count);
    }

    private long BuildWriteModels()
    {
        var position = _nextPosition;

        foreach (var request in _requests)
        {
            if (request.Events.Length == 0)
                continue;

            if (IsProcessed(request.CommitId))
                continue;

            var streamVersion = _streamVersions.GetValueOrDefault(request.StreamId, -1);

            // OCC check: convert the raw long to a StreamState and use the existing Matches logic.
            var actualStreamState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            if (!request.ExpectedStreamState.Matches(actualStreamState))
            {
                logger.LogWarning(
                    "Commit {CommitId} rejected for stream '{StreamId}': " +
                    "expected {ExpectedState}, actual {ActualState}",
                    request.CommitId,
                    request.StreamId,
                    request.ExpectedStreamState,
                    actualStreamState);

                _rejections.Add(request.CommitId, new CommitRejection
                {
                    CommitId = request.CommitId,
                    StreamId = request.StreamId,
                    ExpectedStreamState = request.ExpectedStreamState,
                    ActualStreamState = actualStreamState
                });

                continue;
            }

            foreach (var e in request.Events)
                _writeModels.Add(CreateEventWrite(position++, request, ++streamVersion, e));

            _streamVersions[request.StreamId] = streamVersion;
            _appendedCommitIds.Add(request.CommitId);
        }

        if (HasCommits())
            _writeModels.Add(CreateBatchCompletedWrite(position++));

        return position;

        bool IsProcessed(Guid commitId) =>
            _appendedCommitIds.Contains(commitId) ||
            _duplicateCommitIds.Contains(commitId) ||
            _rejections.ContainsKey(commitId);

        bool HasCommits() =>
            _appendedCommitIds.Count > 0 ||
            _duplicateCommitIds.Count > 0 ||
            _rejections.Count > 0;
    }

    private WriteModel<EventLogEntry> CreateEventWrite(
        long position,
        AppendRequest request,
        long streamVersion,
        PendingEvent @event)
    {
        var filter = CreateEpochGuardFilter(position);

        var update = Builders<EventLogEntry>.Update
            .Set(x => x.Position, position)
            .Set(x => x.Epoch, epoch)
            .Set(x => x.StreamId, request.StreamId)
            .Set(x => x.StreamVersion, streamVersion)
            .Set(x => x.CommitId, request.CommitId)
            .Set(x => x.EventName, @event.EventName)
            .Set(x => x.EventData, @event.EventData)
            .Set(x => x.Metadata, @event.Metadata)
            .CurrentDate(x => x.WrittenAtUtc);

        return new UpdateOneModel<EventLogEntry>(filter, update) { IsUpsert = true };
    }

    private WriteModel<EventLogEntry> CreateBatchCompletedWrite(long position)
    {
        var filter = CreateEpochGuardFilter(position);

        var batchCompleted = new AppendBatchCompleted
        {
            Appends = _appendedCommitIds,
            Duplicates = _duplicateCommitIds,
            Rejections = _rejections.Values,
        };

        var update = Builders<EventLogEntry>.Update
            .Set(x => x.Position, position)
            .Set(x => x.Epoch, epoch)
            .Set(x => x.StreamId, string.Empty)
            .Set(x => x.StreamVersion, 0L)
            .Set(x => x.CommitId, Guid.Empty)
            .Set(x => x.EventName, "AppendBatchCompleted")
            .Set(x => x.EventData, batchCompleted.ToBsonDocument())
            .Set(x => x.Metadata, BsonNull.Value)
            .CurrentDate(x => x.WrittenAtUtc);

        return new UpdateOneModel<EventLogEntry>(filter, update) { IsUpsert = true };
    }

    private FilterDefinition<EventLogEntry> CreateEpochGuardFilter(long position)
    {
        return Builders<EventLogEntry>.Filter.Lt(x => x.Epoch, epoch) &
               Builders<EventLogEntry>.Filter.Eq(x => x.Position, position);
    }
}