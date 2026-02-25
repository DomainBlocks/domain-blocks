using DomainBlocks.Infrastructure.MongoDB.Utilities;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseStore
{
    Task<LeaseWriteResult> AcquireAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<LeaseWriteResult> RenewAsync(
        LeaseClaim claim,
        RenewLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<LeaseWriteResult> ReleaseAsync(LeaseClaim claim, CancellationToken cancellationToken = default);

    Task<LeaseWriteResult> UpdateStateAsync<TState>(
        LeaseClaim claim,
        Action<IScopedUpdateBuilder<TState>> updateState,
        CancellationToken cancellationToken = default);
}