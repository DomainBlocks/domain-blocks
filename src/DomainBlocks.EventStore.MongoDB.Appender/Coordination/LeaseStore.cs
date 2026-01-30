using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

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
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now + options.Duration;
        var holderId = $"{options.HolderIdPrefix}:{Guid.CreateVersion7():N}";

        var existingIsExpired = filterBuilder.Lte(x => x.ExpiresAtUtc, now);
        var existingHasLowerPriority = filterBuilder.Lt(x => x.HolderPriority, options.HolderPriority);
        var existingHasEqualPriority = filterBuilder.Eq(x => x.HolderPriority, options.HolderPriority);
        var weWinTiebreak = filterBuilder.Lt(x => x.HolderId, holderId);
        var existingIsPastMinAge = filterBuilder.Lte(x => x.HeldSinceUtc, now - options.MinHolderAge);
        var existingIsNearExpiry = filterBuilder.Lte(x => x.ExpiresAtUtc, now + options.PreemptWindow);
        var priorityAllowsPreemption = existingHasLowerPriority | (existingHasEqualPriority & weWinTiebreak);
        var timingAllowsPreemption = existingIsPastMinAge & existingIsNearExpiry;
        var canPreempt = priorityAllowsPreemption & timingAllowsPreemption;

        var filter = filterBuilder.Eq(x => x.ResourceId, resourceId) & (existingIsExpired | canPreempt);

        var update = Builders<LeaseState>.Update
            .SetOnInsert(x => x.ResourceId, resourceId)
            .Set(x => x.HolderId, holderId)
            .Set(x => x.HolderPriority, options.HolderPriority)
            .Inc(x => x.Epoch, 1)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.HeldSinceUtc, now)
            .Set(x => x.ExpiresAtUtc, expiresAt);

        var findOneAndUpdatedOptions = new FindOneAndUpdateOptions<LeaseState>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        LeaseState? leaseState = null;

        try
        {
            leaseState = await leaseStates.FindOneAndUpdateAsync(
                filter,
                update,
                findOneAndUpdatedOptions,
                cancellationToken);
        }
        catch (MongoCommandException ex) when (ex.Code == 11000) // duplicate key
        {
            // Another contender acquired the lease.
        }

        return leaseState;
    }

    public async Task<LeaseState?> RenewAsync(
        string resourceId,
        string holderId,
        long epoch,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RenewLeaseOptions.Default;

        var filterBuilder = Builders<LeaseState>.Filter;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now + options.Duration;

        // Renew only if we still hold the lease and the epoch matches.
        var filter = filterBuilder.Eq(x => x.ResourceId, resourceId) &
                     filterBuilder.Eq(x => x.HolderId, holderId) &
                     filterBuilder.Eq(x => x.Epoch, epoch) &
                     filterBuilder.Gt(x => x.ExpiresAtUtc, now);

        var update = Builders<LeaseState>.Update
            .Set(x => x.ExpiresAtUtc, expiresAt)
            .Set(x => x.UpdatedAtUtc, now);

        if (options.HolderPriority.HasValue)
            update = update.Set(x => x.HolderPriority, options.HolderPriority.Value);

        var findOneAndUpdatedOptions = new FindOneAndUpdateOptions<LeaseState>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        };

        var leaseState = await leaseStates.FindOneAndUpdateAsync(
            filter,
            update,
            findOneAndUpdatedOptions,
            cancellationToken);

        return leaseState;
    }

    public async Task<bool> TryReleaseAsync(
        string resourceId,
        string holderId,
        long epoch,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<LeaseState>.Filter;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = filterBuilder.Eq(x => x.ResourceId, resourceId) &
                     filterBuilder.Eq(x => x.HolderId, holderId) &
                     filterBuilder.Eq(x => x.Epoch, epoch);

        var update = Builders<LeaseState>.Update
            .Set(x => x.ExpiresAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now);

        var result = await leaseStates.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<LeaseState?> GetStateAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<LeaseState>.Filter.Eq(x => x.ResourceId, resourceId);
        return await leaseStates.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }
}