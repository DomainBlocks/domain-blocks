using DomainBlocks.Infrastructure.MongoDB.Leases.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseStore : ILeaseStore
{
    private readonly IMongoCollection<LeaseState> _leaseStates;
    private readonly TimeProvider _timeProvider;

    // ReSharper disable once ConvertToPrimaryConstructor - hide leaseStates
    public LeaseStore(IMongoCollection<LeaseState> leaseStates, TimeProvider? timeProvider = null)
    {
        _leaseStates = leaseStates.WithWriteConcern(WriteConcern.WMajority.With(journal: true));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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
            .SetOnInsert(x => x.Counters, [])
            .Set(x => x.HolderId, holderId)
            .Inc(x => x.Epoch, 1)
            .Set(x => x.ContentionPriority, options.ContentionPriority)
            .Set(x => x.HeldSinceUtc, utcNow)
            .Set(x => x.ExpiresAtUtc, expiresAt)
            .Set(x => x.LastMutation.MutationKind, LeaseStateMutationKind.Acquire)
            .Set(x => x.LastMutation.MutatedAtUtc, utcNow);

        var findOneAndUpdatedOptions = new FindOneAndUpdateOptions<LeaseState>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After,
        };

        LeaseState? state = null;

        try
        {
            state = await _leaseStates
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
        LeaseClaim claim,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RenewLeaseOptions.Default;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetLeaseMatchesClaimFilter(claim, utcNow);
        var expiresAt = utcNow + options.Duration;

        var update = Builders<LeaseState>.Update
            .Set(x => x.ExpiresAtUtc, expiresAt)
            .Set(x => x.LastMutation.MutationKind, LeaseStateMutationKind.Renew)
            .Set(x => x.LastMutation.MutatedAtUtc, utcNow);

        if (options.ContentionPriority.HasValue)
            update = update.Set(x => x.ContentionPriority, options.ContentionPriority.Value);

        var findOneAndUpdatedOptions = new FindOneAndUpdateOptions<LeaseState>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        };

        var state = await _leaseStates
            .FindOneAndUpdateAsync(
                filter,
                update,
                findOneAndUpdatedOptions,
                cancellationToken)
            .ConfigureAwait(false);

        return state;
    }

    public async Task<bool> TryIncrementCounterAsync(
        LeaseClaim claim,
        string counterName,
        long delta,
        CancellationToken cancellationToken = default)
    {
        if (counterName.Contains('.'))
        {
            throw new ArgumentException(
                $"Invalid counter name '{counterName}'. Counter names must not contain '.' (dot).",
                nameof(counterName));
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetLeaseMatchesClaimFilter(claim, utcNow);

        var update = Builders<LeaseState>.Update
            .Inc($"{LeaseStateFieldNames.Counters}.{counterName}", delta)
            .Set(x => x.LastMutation.MutationKind, LeaseStateMutationKind.IncrementCounter)
            .Set(x => x.LastMutation.MutatedAtUtc, utcNow);

        var result = await _leaseStates
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.MatchedCount == 1;
    }

    public async Task<bool> TryReleaseAsync(LeaseClaim claim, CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetLeaseMatchesClaimFilter(claim, utcNow);

        var update = Builders<LeaseState>.Update
            .Set(x => x.ExpiresAtUtc, utcNow)
            .Set(x => x.LastMutation.MutationKind, LeaseStateMutationKind.Release)
            .Set(x => x.LastMutation.MutatedAtUtc, utcNow);

        var result = await _leaseStates
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.MatchedCount == 1;
    }

    private static FilterDefinition<LeaseState> GetLeaseMatchesClaimFilter(LeaseClaim claim, DateTime utcNow)
    {
        var filterBuilder = Builders<LeaseState>.Filter;

        return filterBuilder.Eq(x => x.ResourceId, claim.ResourceId) &
               filterBuilder.Eq(x => x.HolderId, claim.HolderId) &
               filterBuilder.Eq(x => x.Epoch, claim.Epoch) &
               filterBuilder.Gt(x => x.ExpiresAtUtc, utcNow);
    }
}