namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ICommitTracker
{
    Task WaitAsync(Guid commitId, CancellationToken cancellationToken = default);
}