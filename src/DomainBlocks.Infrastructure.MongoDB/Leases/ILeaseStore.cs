using DomainBlocks.Infrastructure.MongoDB.Leases.Schema;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseStore
{
    Task<LeaseState?> AcquireAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<LeaseState?> RenewAsync(
        LeaseClaim claim,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<bool> TryIncrementCounterAsync(
        LeaseClaim claim,
        string counterName,
        long delta,
        CancellationToken cancellationToken = default);

    Task<bool> TryReleaseAsync(LeaseClaim claim, CancellationToken cancellationToken = default);
}