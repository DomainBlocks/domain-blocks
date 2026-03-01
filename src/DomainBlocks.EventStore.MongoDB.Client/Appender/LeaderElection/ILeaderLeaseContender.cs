namespace DomainBlocks.EventStore.MongoDB.Client.Appender.LeaderElection;

public interface ILeaderLeaseContender : IAsyncDisposable
{
    Task RunAsync(ILeaderLeaseObserver observer, CancellationToken stopToken = default);
}