namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseClient
{
    Task<AcquireLeaseResult<ILeaseHandle>> AcquireLeaseAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<AcquireLeaseResult<ILeaseHandle<TState>>> AcquireLeaseAsync<TState>(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);
}