namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamSubscription : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task WaitForCompletedAsync(CancellationToken cancellationToken = default);
}