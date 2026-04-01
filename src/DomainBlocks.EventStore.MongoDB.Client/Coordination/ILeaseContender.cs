namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ILeaseContender
{
    Task RunAsync(ILeaseListener listener, CancellationToken cancellationToken = default);
}