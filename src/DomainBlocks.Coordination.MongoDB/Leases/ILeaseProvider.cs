namespace DomainBlocks.Coordination.MongoDB.Leases;

public interface ILeaseProvider
{
    Task<ILease?> AcquireLeaseAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);
}