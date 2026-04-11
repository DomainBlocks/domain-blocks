using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class LeaseStore(IMongoCollection<LeaseDocument> leases, TimeProvider? timeProvider = null)
{
    private static readonly FindOneAndUpdateOptions<LeaseDocument> UpsertReturnAfterOptions = new()
    {
        IsUpsert = true,
        ReturnDocument = ReturnDocument.After
    };

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<LeaseDocument?> AcquireAsync(
        string holderIdPrefix,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var holderId = $"{holderIdPrefix}:{Guid.CreateVersion7():N}";
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<LeaseDocument>.Filter.Eq(x => x.Id, LeaseDocument.LeaseId) &
                     Builders<LeaseDocument>.Filter.Lte(x => x.ExpiresAtUtc, now);

        var update = Builders<LeaseDocument>.Update
            .Set(x => x.HolderId, holderId)
            .Inc(x => x.Epoch, 1)
            .Set(x => x.AcquiredAtUtc, now)
            .Set(x => x.ExpiresAtUtc, now + duration)
            .Set(x => x.LastUpdatedAtUtc, now);

        try
        {
            return await leases
                .FindOneAndUpdateAsync(filter, update, UpsertReturnAfterOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.Code == MongoErrorCodes.DuplicateKey)
        {
            return null; // Another contender won the race.
        }
    }

    public async Task<bool> TryRenewAsync(
        string holderId,
        long epoch,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = BuildHolderFilter(holderId, epoch, now);

        var update = Builders<LeaseDocument>.Update
            .Set(x => x.ExpiresAtUtc, now + duration)
            .Set(x => x.LastUpdatedAtUtc, now);

        var result = await leases
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryReleaseAsync(
        string holderId,
        long epoch,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = BuildHolderFilter(holderId, epoch, now);

        var update = Builders<LeaseDocument>.Update
            .Set(x => x.ExpiresAtUtc, now) // Expire immediately.
            .Set(x => x.LastUpdatedAtUtc, now);

        var result = await leases
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryAdvanceCommitPositionAsync(
        string holderId,
        long epoch,
        long count,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = BuildHolderFilter(holderId, epoch, now);

        var update = Builders<LeaseDocument>.Update
            .Inc(x => x.CommitPosition, count)
            .Set(x => x.LastUpdatedAtUtc, now);

        var result = await leases
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.ModifiedCount == 1;
    }

    private static FilterDefinition<LeaseDocument> BuildHolderFilter(string holderId, long epoch, DateTime now)
    {
        var builder = Builders<LeaseDocument>.Filter;

        return builder.Eq(x => x.Id, LeaseDocument.LeaseId) &
               builder.Eq(x => x.HolderId, holderId) &
               builder.Eq(x => x.Epoch, epoch) &
               builder.Gt(x => x.ExpiresAtUtc, now);
    }
}