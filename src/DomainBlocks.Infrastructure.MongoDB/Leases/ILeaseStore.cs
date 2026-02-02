namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseStore
{
    Task<LeaseState?> AcquireAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<LeaseState?> RenewAsync(
        string resourceId,
        string holderId,
        long epoch,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<bool> TryReleaseAsync(
        string resourceId,
        string holderId,
        long epoch,
        CancellationToken cancellationToken = default);

    Task<LeaseState?> GetStateAsync(string resourceId, CancellationToken cancellationToken = default);
}