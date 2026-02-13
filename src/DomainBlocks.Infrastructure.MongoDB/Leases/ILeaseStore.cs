using DomainBlocks.Infrastructure.MongoDB.Sequences;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseStore
{
    Task<LeaseState?> AcquireAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<LeaseState?> RenewAsync(
        LeaseToken token,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<bool> TryFenceAsync(
        IClientSessionHandle session,
        LeaseToken token,
        CancellationToken cancellationToken = default);

    Task<SequenceRange?> NextSequenceRangeAsync(
        LeaseToken token,
        long count,
        CancellationToken cancellationToken = default);

    Task<SequenceRange?> NextSequenceRangeAsync(
        IClientSessionHandle session,
        LeaseToken token,
        long count,
        CancellationToken cancellationToken = default);

    Task<bool> TryReleaseAsync(LeaseToken token, CancellationToken cancellationToken = default);
}