namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ILeaseContender
{
    Task RunAsync(IReadOnlyCollection<ILeaseObserver> observers, CancellationToken cancellationToken = default);
}