using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppender(
    IMongoCollection<LoggedEvent> loggedEvents,
    long epoch,
    long? initialCommitPosition,
    ILogger<EventAppender> logger)
{
    // Committed events from any epoch, plus our own uncommitted writes.
    private readonly FilterDefinition<LoggedEvent> _visibilityFilter = initialCommitPosition.HasValue
        ? Builders<LoggedEvent>.Filter.Lte(x => x.Position, initialCommitPosition.Value) |
          Builders<LoggedEvent>.Filter.Eq(x => x.Epoch, epoch)
        : Builders<LoggedEvent>.Filter.Eq(x => x.Epoch, epoch);

    private long _nextPosition = initialCommitPosition.HasValue ? initialCommitPosition.Value + 1 : 0;

    // Solving idempotency:
    // - When any leader steps up, first ensure that request statuses are reconciled and up-to-date based on event log.
    // - Do not process any pending requests until this has happened, i.e. all requests must be genuinely pending.
    // - Via the change stream, only process inserted requests.
    // - This assumes exactly-once semantics via the change stream.
    // - Live overlap with catch-up may be an issue - keep a HashSet of processed commit IDs from catch-up.
    public async Task AppendEventsAsync(
        IEnumerable<AppendRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var models = new List<WriteModel<LoggedEvent>>();

        foreach (var request in requests)
        {
            // if (await IsCommitAlreadyWrittenAsync(request.CommitId, cancellationToken).ConfigureAwait(false))
            //     continue;

            // TODO: Cache
            var nextStreamVersion = await GetNextStreamVersionAsync(request.StreamId, cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < request.Events.Length; i++)
            {
                var evt = request.Events[i];

                var position = _nextPosition++;

                var filter = Builders<LoggedEvent>.Filter.Eq(x => x.Position, position) &
                             Builders<LoggedEvent>.Filter.Lt(x => x.Epoch, epoch);

                var update = Builders<LoggedEvent>.Update
                    .Set(x => x.Position, position)
                    .Set(x => x.Epoch, epoch)
                    .Set(x => x.StreamId, request.StreamId)
                    .Set(x => x.StreamVersion, nextStreamVersion++)
                    .Set(x => x.CommitId, request.CommitId)
                    .Set(x => x.CommitIndex, i)
                    .Set(x => x.EventName, evt.EventName)
                    .Set(x => x.EventData, evt.EventData)
                    .Set(x => x.Metadata, evt.Metadata)
                    .CurrentDate(x => x.WrittenAtUtc);

                models.Add(new UpdateOneModel<LoggedEvent>(filter, update) { IsUpsert = true });
            }
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

        var result = await loggedEvents
            .BulkWriteAsync(models, bulkWriteOptions, cancellationToken)
            .ConfigureAwait(false);

        // Success = every model matched (upserted or replaced). The count includes sentinel.
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

    private async Task<bool> IsCommitAlreadyWrittenAsync(
        Guid commitId,
        CancellationToken cancellationToken)
    {
        var commitFilter = Builders<LoggedEvent>.Filter.Eq(x => x.CommitId, commitId);

        return await loggedEvents
            .Find(commitFilter & _visibilityFilter)
            .Limit(1)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<long> GetNextStreamVersionAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        var streamFilter = Builders<LoggedEvent>.Filter.Eq(x => x.StreamId, streamId);

        // Stream version correctness:
        // Committed events (Position <= initialCommitPosition): canonical, immutable - correct by definition.
        // Our epoch's events (Epoch == epoch): we assigned versions sequentially - correct by construction.

        var latestVersion = await loggedEvents
            .Find(streamFilter & _visibilityFilter)
            .Sort(Builders<LoggedEvent>.Sort.Descending(x => x.StreamVersion))
            .Limit(1)
            .Project(x => (long?)x.StreamVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return latestVersion.HasValue ? latestVersion.Value + 1 : 0;
    }
}