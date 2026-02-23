namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseStore
{
    Task<LeaseDocument?> AcquireAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<LeaseDocument?> RenewAsync(
        LeaseClaim claim,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<bool> TryReleaseAsync(LeaseClaim claim, CancellationToken cancellationToken = default);

    Task<bool> TryUpdateStateAsync<TState>(
        LeaseClaim claim,
        Action<IScopedUpdateBuilder<TState>> updateState,
        CancellationToken cancellationToken = default);
}