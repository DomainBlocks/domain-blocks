using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public interface ISubscriptionConsumer
{
    Task OnInitializing(CancellationToken cancellationToken);
    Task OnSubscribing(CancellationToken cancellationToken);
    Task OnSubscriptionDropped(Exception? exception, CancellationToken cancellationToken);
    Task OnEvent(ReadEvent readEvent, CancellationToken cancellationToken);
}