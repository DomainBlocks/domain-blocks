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
        if (requestArray.Length == 0)
            return;

        var allCommitIds = requestArray.Select(x => x.CommitId).Distinct();
        var allStreamIds = requestArray.Select(x => x.StreamId).Distinct();

        var (commitIds, streamStates) = await GetBatchContextAsync(allCommitIds, allStreamIds, cancellationToken)
            .ConfigureAwait(false);

        var nextPosition = _nextPosition;

        var models = new List<WriteModel<EventLogEntry>>();
        var appendedCommitIds = new List<Guid>();
        var rejectedCommitIds = new List<Guid>();
        var duplicateCommitIds = new List<Guid>();

        foreach (var request in requestArray)
        {
            if (request.Events.Length == 0)
                continue;

            if (!commitIds.Add(request.CommitId))
            {
                duplicateCommitIds.Add(request.CommitId);
                continue;
            }

            // TODO: Check expected state matches
            var streamState = streamStates.GetValueOrDefault(request.StreamId);
            var streamVersionValue = streamState.IsStreamExists ? streamState.Version.Value.ToInt64() : -1;

            foreach (var e in request.Events)
            {
                var position = nextPosition++;
                var filter = CreateEventLogFilter(position);

                var update = CreateEventLogUpdate(
                    position,
                    request.StreamId,
                    ++streamVersionValue,
                    request.CommitId,
                    e.EventName,
                    e.EventData,
                    e.Metadata);

                models.Add(new UpdateOneModel<EventLogEntry>(filter, update) { IsUpsert = true });
            }

            var streamVersion = StreamVersion.FromInt64(streamVersionValue);
            streamStates[request.StreamId] = StreamState.StreamExists(streamVersion);

            appendedCommitIds.Add(request.CommitId);
        }

        if (appendedCommitIds.Count == 0 && rejectedCommitIds.Count == 0 && duplicateCommitIds.Count == 0)
            return;

        // Mark the end of the batch so another process can advance the commit position.
        {
            var position = nextPosition++;
            var filter = CreateEventLogFilter(position);

            var batchCompleted = new AppendBatchCompleted
            {
                AppendedCommitIds = appendedCommitIds,
                RejectedCommitIds = rejectedCommitIds,
                DuplicateCommitIds = duplicateCommitIds
            };

            var update = CreateEventLogUpdate(
                position,
                string.Empty,
                0,
                Guid.Empty,
                "AppendBatchCompleted",
                batchCompleted.ToBsonDocument(),
                BsonNull.Value);

            models.Add(new UpdateOneModel<EventLogEntry>(filter, update) { IsUpsert = true });
        }

        var result = await eventLog
            .BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = true }, cancellationToken)
            .ConfigureAwait(false);

        // Success = every model matched (upserted or replaced).
        var expected = models.Count;
        var written = result.Upserts.Count + result.ModifiedCount;

        // TODO: Tidy this up. What should the behaviour be?
        if (written != expected)
            throw new Exception($"Batch partially written: expected {expected}, written {written}.");

        _nextPosition = nextPosition;
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

    private FilterDefinition<EventLogEntry> CreateEventLogFilter(long position)
    {
        return Builders<EventLogEntry>.Filter.Eq(x => x.Position, position) &
               Builders<EventLogEntry>.Filter.Lt(x => x.Epoch, epoch);
    }

    private UpdateDefinition<EventLogEntry> CreateEventLogUpdate(
        long position,
        string streamId,
        long streamVersion,
        Guid commitId,
        string eventName,
        BsonValue eventData,
        BsonValue metadata)
    {
        return Builders<EventLogEntry>.Update
            .Set(x => x.Position, position)
            .Set(x => x.Epoch, epoch)
            .Set(x => x.StreamId, streamId)
            .Set(x => x.StreamVersion, streamVersion)
            .Set(x => x.CommitId, commitId)
            .Set(x => x.EventName, eventName)
            .Set(x => x.EventData, eventData)
            .Set(x => x.Metadata, metadata)
            .CurrentDate(x => x.WrittenAtUtc);
    }

    private record BatchContext(HashSet<Guid> CommitIds, Dictionary<string, StreamState> StreamStates);
}