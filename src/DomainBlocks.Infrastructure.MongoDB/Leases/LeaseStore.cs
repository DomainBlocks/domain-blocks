using DomainBlocks.Infrastructure.MongoDB.Errors;
using DomainBlocks.Infrastructure.MongoDB.Utilities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public class LeaseStore : ILeaseStore
{
    private static readonly FindOneAndUpdateOptions<LeaseDocument, RawBsonDocument> UpsertOptions =
        CreateFindOneAndUpdateOptions(isUpsert: true);

    private static readonly FindOneAndUpdateOptions<LeaseDocument, RawBsonDocument> UpdateOptions =
        CreateFindOneAndUpdateOptions(isUpsert: false);

    private readonly IMongoCollection<LeaseDocument> _leases;
    private readonly TimeProvider _timeProvider;

    // ReSharper disable once ConvertToPrimaryConstructor - hide leases arg
    public LeaseStore(IMongoCollection<LeaseDocument> leases, TimeProvider? timeProvider = null)
    {
        _leases = leases.WithWriteConcern(WriteConcern.WMajority.With(journal: true));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LeaseWriteResult> AcquireAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AcquireLeaseOptions.Default;

        var filterBuilder = Builders<LeaseDocument>.Filter;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = utcNow + options.Duration;
        var holderId = $"{options.HolderIdPrefix}:{Guid.CreateVersion7():N}";

        var existingIsExpired = filterBuilder.Lte(x => x.ExpiresAtUtc, utcNow);
        var existingHasLowerPriority = filterBuilder.Lt(x => x.ContentionPriority, options.ContentionPriority);
        var existingHasMinTenure = filterBuilder.Lte(x => x.HeldSinceUtc, utcNow - options.MinTenure);
        var canPreempt = existingHasLowerPriority & existingHasMinTenure;

        var filter = filterBuilder.Eq(x => x.ResourceId, resourceId) & (existingIsExpired | canPreempt);

        var update = Builders<LeaseDocument>.Update
            .SetOnInsert(x => x.ResourceId, resourceId)
            .Set(x => x.HolderId, holderId)
            .Inc(x => x.Epoch, 1)
            .Set(x => x.ContentionPriority, options.ContentionPriority)
            .Set(x => x.HeldSinceUtc, utcNow)
            .Set(x => x.ExpiresAtUtc, expiresAt)
            .SetOnInsert(x => x.State, [])
            .Set(x => x.LastUpdatedAtUtc, utcNow)
            .Set(x => x.LastUpdateKind, LeaseUpdateKind.Acquired);

        try
        {
            var doc = await _leases
                .FindOneAndUpdateAsync(filter, update, UpsertOptions, cancellationToken)
                .ConfigureAwait(false);

            if (doc is not null)
                return LeaseWriteResult.Success(new LeaseSnapshotView(doc));
        }
        catch (MongoCommandException ex) when (ex.Code == ErrorCodes.DuplicateKey)
        {
            // Another contender acquired the lease.
        }

        return LeaseWriteResult.NotHeld();
    }

    public async Task<LeaseWriteResult> RenewAsync(
        LeaseClaim claim,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RenewLeaseOptions.Default;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetMatchesClaimFilter(claim, utcNow);
        var expiresAt = utcNow + options.Duration;

        var update = Builders<LeaseDocument>.Update
            .Set(x => x.ExpiresAtUtc, expiresAt)
            .Set(x => x.LastUpdatedAtUtc, utcNow)
            .Set(x => x.LastUpdateKind, LeaseUpdateKind.Renewed);

        if (options.ContentionPriority.HasValue)
            update = update.Set(x => x.ContentionPriority, options.ContentionPriority.Value);

        var doc = await _leases
            .FindOneAndUpdateAsync(filter, update, UpdateOptions, cancellationToken)
            .ConfigureAwait(false);

        return doc is not null ? LeaseWriteResult.Success(new LeaseSnapshotView(doc)) : LeaseWriteResult.NotHeld();
    }

    public async Task<LeaseWriteResult> ReleaseAsync(LeaseClaim claim, CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetMatchesClaimFilter(claim, utcNow);

        var update = Builders<LeaseDocument>.Update
            .Set(x => x.ExpiresAtUtc, utcNow)
            .Set(x => x.LastUpdatedAtUtc, utcNow)
            .Set(x => x.LastUpdateKind, LeaseUpdateKind.Released);

        var doc = await _leases
            .FindOneAndUpdateAsync(filter, update, UpdateOptions, cancellationToken)
            .ConfigureAwait(false);

        return doc is not null ? LeaseWriteResult.Success(new LeaseSnapshotView(doc)) : LeaseWriteResult.NotHeld();
    }

    public async Task<LeaseWriteResult> UpdateStateAsync<TState>(
        LeaseClaim claim,
        Action<IScopedUpdateBuilder<TState>> updateState,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var filter = GetMatchesClaimFilter(claim, utcNow);

        var scopedUpdateBuilder = ScopedUpdate
            .For<LeaseDocument>()
            .At(x => x.State)
            .As<TState>();

        updateState(scopedUpdateBuilder);

        var updateDefinition = scopedUpdateBuilder
            .Build()
            .Set(x => x.LastUpdatedAtUtc, utcNow)
            .Set(x => x.LastUpdateKind, LeaseUpdateKind.StateUpdated);

        var doc = await _leases
            .FindOneAndUpdateAsync(filter, updateDefinition, UpdateOptions, cancellationToken)
            .ConfigureAwait(false);

        return doc is not null ? LeaseWriteResult.Success(new LeaseSnapshotView(doc)) : LeaseWriteResult.NotHeld();
    }

    private static FindOneAndUpdateOptions<LeaseDocument, RawBsonDocument> CreateFindOneAndUpdateOptions(bool isUpsert)
    {
        return new FindOneAndUpdateOptions<LeaseDocument, RawBsonDocument>
        {
            IsUpsert = isUpsert,
            Projection = Builders<LeaseDocument>.Projection.As<RawBsonDocument>(),
            ReturnDocument = ReturnDocument.After
        };
    }

    private static FilterDefinition<LeaseDocument> GetMatchesClaimFilter(LeaseClaim claim, DateTime utcNow)
    {
        var filterBuilder = Builders<LeaseDocument>.Filter;

        return filterBuilder.Eq(x => x.ResourceId, claim.ResourceId) &
               filterBuilder.Eq(x => x.HolderId, claim.HolderId) &
               filterBuilder.Eq(x => x.Epoch, claim.Epoch) &
               filterBuilder.Gt(x => x.ExpiresAtUtc, utcNow);
    }
}