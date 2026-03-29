namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ILeaseContender
{
    Task RunAsync(ILeaseObserver observer, CancellationToken cancellationToken = default);
}