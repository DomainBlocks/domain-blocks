using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseFencer(IMongoCollection<LeaseState> leaseStates, TimeProvider? timeProvider = null) :
    ILeaseFencer
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<bool> TryFenceAsync(
        IClientSessionHandle session,
        string resourceId,
        string holderId,
        long epoch,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<LeaseState>.Filter;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = filterBuilder.Eq(x => x.ResourceId, resourceId) &
                     filterBuilder.Eq(x => x.HolderId, holderId) &
                     filterBuilder.Eq(x => x.Epoch, epoch) &
                     filterBuilder.Gt(x => x.ExpiresAtUtc, now);

        var update = Builders<LeaseState>.Update.Set(x => x.UpdatedAtUtc, now);

        var result = await leaseStates
            .UpdateOneAsync(
                session,
                filter,
                update,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.MatchedCount == 1;
    }
}