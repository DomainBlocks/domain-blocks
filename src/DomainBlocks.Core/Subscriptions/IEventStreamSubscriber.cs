namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamSubscriber<in TEvent, in TPosition>
{
    Task OnCatchingUp(CancellationToken cancellationToken);

    Task OnEvent(TEvent @event, TPosition position, CancellationToken cancellationToken);

    Task OnLive(CancellationToken cancellationToken);

    Task OnSubscriptionDropped(
        SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken);
}