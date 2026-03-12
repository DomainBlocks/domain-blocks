namespace DomainBlocks.EventStore.MongoDB.Client.Appender.LeaderElection;

public interface ILeaderLeaseContender : IAsyncDisposable
{
    Task RunAsync(ILocalLeaseObserver observer, CancellationToken stopToken = default);
}