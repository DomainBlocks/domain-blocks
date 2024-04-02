namespace DomainBlocks.V1.Subscriptions;

public interface ISubscriptionConsumer
{
    Task OnInitializingAsync(CancellationToken cancellationToken);
    Task OnSubscribingAsync(CancellationToken cancellationToken);
    Task OnSubscriptionDroppedAsync(Exception? exception, CancellationToken cancellationToken);
    Task OnEventAsync(object @event, CancellationToken cancellationToken);
}