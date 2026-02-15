using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseStore(IMongoCollection<LeaseState> leaseStates, TimeProvider? timeProvider = null) :
    ILeaseStore
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<LeaseState?> AcquireAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AcquireLeaseOptions.Default;

        var filterBuilder = Builders<LeaseState>.Filter;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = utcNow + options.Duration;
        var holderId = $"{options.HolderIdPrefix}:{Guid.CreateVersion7():N}";

        var existingIsExpired = filterBuilder.Lte(x => x.ExpiresAtUtc, utcNow);
        var existingHasLowerPriority = filterBuilder.Lt(x => x.ContentionPriority, options.ContentionPriority);
        var existingHasMinTenure = filterBuilder.Lte(x => x.HeldSinceUtc, utcNow - options.MinTenure);
        var canPreempt = existingHasLowerPriority & existingHasMinTenure;

        var filter = filterBuilder.Eq(x => x.ResourceId, resourceId) & (existingIsExpired | canPreempt);

        var update = Builders<LeaseState>.Update
            .SetOnInsert(x => x.ResourceId, resourceId)
            .Set(x => x.HolderId, holderId)
            .Inc(x => x.Epoch, 1)
            .Set(x => x.ContentionPriority, options.ContentionPriority)
            .Set(x => x.UpdatedAtUtc, utcNow)
            .Set(x => x.HeldSinceUtc, utcNow)
            .Set(x => x.ExpiresAtUtc, expiresAt);

        var findOneAndUpdatedOptions = new FindOneAndUpdateOptions<LeaseState>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        LeaseState? state = null;

        try
        {
            state = await leaseStates
                .FindOneAndUpdateAsync(
                    filter,
                    update,
                    findOneAndUpdatedOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.Code == 11000) // duplicate key
        {
            // Another contender acquired the lease.
        }

        return state;
    }

    public async Task<LeaseState?> RenewAsync(
        LeaseToken token,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RenewLeaseOptions.Default;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetHeldLeaseFilter(token, utcNow);
        var expiresAt = utcNow + options.Duration;

        var update = Builders<LeaseState>.Update
            .Set(x => x.ExpiresAtUtc, expiresAt)
            .Set(x => x.UpdatedAtUtc, utcNow);

        if (options.ContentionPriority.HasValue)
            update = update.Set(x => x.ContentionPriority, options.ContentionPriority.Value);

        var findOneAndUpdatedOptions = new FindOneAndUpdateOptions<LeaseState>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        };

        var state = await leaseStates
            .FindOneAndUpdateAsync(
                filter,
                update,
                findOneAndUpdatedOptions,
                cancellationToken)
            .ConfigureAwait(false);

        return state;
    }

    public async Task<bool> TryReleaseAsync(
        LeaseToken token,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetHeldLeaseFilter(token, utcNow);

        var update = Builders<LeaseState>.Update
            .Set(x => x.ExpiresAtUtc, utcNow)
            .Set(x => x.UpdatedAtUtc, utcNow);

        var result = await leaseStates
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.ModifiedCount == 1;
    }

    private static FilterDefinition<LeaseState> GetHeldLeaseFilter(LeaseToken token, DateTime? utcNow = null)
    {
        var filterBuilder = Builders<LeaseState>.Filter;

        var filter = filterBuilder.Eq(x => x.ResourceId, token.ResourceId) &
                     filterBuilder.Eq(x => x.HolderId, token.HolderId) &
                     filterBuilder.Eq(x => x.Epoch, token.Epoch);

        if (utcNow.HasValue)
            filter &= filterBuilder.Gt(x => x.ExpiresAtUtc, utcNow);

        return filter;
    }
}