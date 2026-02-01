namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public interface ILeaseProvider
{
    Task<ILease?> AcquireLeaseAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default);
}