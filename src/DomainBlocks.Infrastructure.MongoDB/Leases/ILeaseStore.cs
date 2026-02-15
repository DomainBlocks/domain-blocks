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

    Task<bool> TryReleaseAsync(LeaseToken token, CancellationToken cancellationToken = default);
}