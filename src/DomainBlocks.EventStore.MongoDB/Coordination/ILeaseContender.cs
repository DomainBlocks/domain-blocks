namespace DomainBlocks.EventStore.MongoDB.Coordination;

public interface ILeaseContender
{
    Task RunAsync(ILeaseListener listener, CancellationToken cancellationToken = default);
}