namespace DomainBlocks.V1.Abstractions.Subscriptions;

public interface IEventStreamSubscription : IDisposable
{
    IAsyncEnumerable<SubscriptionMessage> ConsumeAsync(CancellationToken cancellationToken = default);
}