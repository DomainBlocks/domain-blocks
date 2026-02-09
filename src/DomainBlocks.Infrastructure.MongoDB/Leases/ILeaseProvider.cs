namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseProvider
{
    Task<LeaseAcquisition> AcquireLeaseAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);
}