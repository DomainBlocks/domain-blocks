using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppender(
    IMongoCollection<EventLogEntry> eventLog,
    long epoch,
    long? initialCommitPosition,
    ILogger<EventAppender> logger)
{
    // Committed events from any epoch, plus our own uncommitted writes.
    private readonly FilterDefinition<EventLogEntry> _visibilityFilter = initialCommitPosition.HasValue
        ? Builders<EventLogEntry>.Filter.Lte(x => x.Position, initialCommitPosition.Value) |
          Builders<EventLogEntry>.Filter.Eq(x => x.Epoch, epoch)
        : Builders<EventLogEntry>.Filter.Eq(x => x.Epoch, epoch);

    private long _nextPosition = initialCommitPosition.HasValue ? initialCommitPosition.Value + 1 : 0;

    public async Task AppendEventsAsync(
        IEnumerable<AppendRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var requestArray = requests as AppendRequest[] ?? [.. requests];
        var allCommitIds = requestArray.Select(x => x.CommitId).Distinct();
        var allStreamIds = requestArray.Select(x => x.StreamId).Distinct();

        var batchContext = await GetBatchContextAsync(allCommitIds, allStreamIds, cancellationToken)
            .ConfigureAwait(false);

        var models = new List<WriteModel<EventLogEntry>>();

        foreach (var request in requestArray)
        {
            if (request.Events.Length == 0)
                continue;

            if (!batchContext.CommitIds.Add(request.CommitId))
                continue;

            var streamState = batchContext.StreamStates.GetValueOrDefault(request.StreamId);
            var streamVersionValue = streamState.IsStreamExists ? streamState.Version.Value.ToInt64() : -1;

            foreach (var @event in request.Events)
            {
                var position = _nextPosition++;

                var filter = Builders<EventLogEntry>.Filter.Eq(x => x.Position, position) &
                             Builders<EventLogEntry>.Filter.Lt(x => x.Epoch, epoch);

                var update = Builders<EventLogEntry>.Update
                    .Set(x => x.Position, position)
                    .Set(x => x.Epoch, epoch)
                    .Set(x => x.StreamId, request.StreamId)
                    .Set(x => x.StreamVersion, ++streamVersionValue)
                    .Set(x => x.CommitId, request.CommitId)
                    .Set(x => x.EventName, @event.EventName)
                    .Set(x => x.EventData, @event.EventData)
                    .Set(x => x.Metadata, @event.Metadata)
                    .CurrentDate(x => x.WrittenAtUtc);

                models.Add(new UpdateOneModel<EventLogEntry>(filter, update) { IsUpsert = true });
            }

            var streamVersion = StreamVersion.FromInt64(streamVersionValue);
            batchContext.StreamStates[request.StreamId] = StreamState.StreamExists(streamVersion);
        }

        if (models.Count == 0)
            return;

        // Marks the end of the batch so another process can advance the commit position.
        // var batchMarker = Builders<LoggedEvent>.Update
        //     .Set(x => x.Position, nextPosition++)
        //     .Set(x => x.Epoch, epoch)
        //     .Set(x => x.StreamId, string.Empty)
        //     .Set(x => x.StreamVersion, 0)
        //     .Set(x => x.CommitId, Guid.Empty)
        //     .Set(x => x.CommitIndex, 0)
        //     .Set(x => x.EventName, "EventBatchAppended")
        //     .Set(x => x.EventData, BsonNull.Value)
        //     .Set(x => x.Metadata, BsonNull.Value)
        //     .CurrentDate(x => x.WrittenAtUtc);
        //
        // models.Add(new UpdateOneModel<LoggedEvent>(filter, batchMarker) { IsUpsert = true });

        var bulkWriteOptions = new BulkWriteOptions { IsOrdered = true };

        var result = await eventLog
            .BulkWriteAsync(models, bulkWriteOptions, cancellationToken)
            .ConfigureAwait(false);

        // Success = every model matched (upserted or replaced). The count includes EventBatchAppended.
        var totalExpected = models.Count;
        var totalWritten = result.Upserts.Count + result.ModifiedCount;

        if (totalWritten != totalExpected)
        {
            logger.LogWarning(
                "Batch partially written: expected {Expected}, written {Written}. " +
                "A higher epoch may have claimed these positions",
                totalExpected,
                totalWritten);
        }
    }

    private async Task<BatchContext> GetBatchContextAsync(
        IEnumerable<Guid> commitIds,
        IEnumerable<string> streamIds,
        CancellationToken cancellationToken)
    {
        var commitIdsTask = eventLog
            .Distinct(
                x => x.CommitId,
                Builders<EventLogEntry>.Filter.In(x => x.CommitId, commitIds) & _visibilityFilter,
                cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        var maxVersionsTask = eventLog
            .Aggregate()
            .Match(Builders<EventLogEntry>.Filter.In(x => x.StreamId, streamIds) & _visibilityFilter)
            .Group(x => x.StreamId, g => new { StreamId = g.Key, MaxVersion = g.Max(x => x.StreamVersion) })
            .ToListAsync(cancellationToken);

        await Task.WhenAll(commitIdsTask, maxVersionsTask).ConfigureAwait(false);

        var commitIdSet = (await commitIdsTask.ConfigureAwait(false)).ToHashSet();

        var streamStates = (await maxVersionsTask.ConfigureAwait(false))
            .ToDictionary(
                x => x.StreamId,
                x =>
                {
                    var version = StreamVersion.FromInt64(x.MaxVersion);
                    return StreamState.StreamExists(version);
                });

        return new BatchContext(commitIdSet, streamStates);
    }

    private readonly struct BatchContext(HashSet<Guid> commitIds, Dictionary<string, StreamState> streamStates)
    {
        public HashSet<Guid> CommitIds { get; } = commitIds;
        public Dictionary<string, StreamState> StreamStates { get; } = streamStates;
    }
}