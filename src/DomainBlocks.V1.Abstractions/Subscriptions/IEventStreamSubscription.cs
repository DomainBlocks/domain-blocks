using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.Abstractions.Subscriptions;

public interface IEventStreamSubscription : IDisposable
{
    IAsyncEnumerable<ISubscriptionMessage> ConsumeAsync(CancellationToken cancellationToken = default);
}