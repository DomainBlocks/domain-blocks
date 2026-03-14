namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IAppendRequestTracker
{
    Task WaitAsync(Guid commitId, CancellationToken cancellationToken = default);
}