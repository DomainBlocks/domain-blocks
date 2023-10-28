namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamListener<in TEvent, in TPosition>
{
    /// <summary>
    /// Invoked when the subscription is about to process historical events. This is invoked at least once for the
    /// lifetime of a subscription. It will be called just after OnStarting, and then if the subscription is dropped, it
    /// will be invoked prior to each re-subscription attempt. The subscription is considered to be "catching up" until
    /// OnLive is invoked.
    /// </summary>
    Task OnCatchingUp(CancellationToken cancellationToken);

    /// <summary>
    /// Invoked whenever an event is read from the stream.
    /// </summary>
    Task<OnEventResult> OnEvent(TEvent @event, TPosition position, CancellationToken cancellationToken);

    /// <summary>
    /// Invoked once all historical events have been processed.
    /// </summary>
    Task OnLive(CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when the subscription is dropped, either due to a server error, client error, or because the
    /// subscription was disposed.
    /// </summary>
    Task OnSubscriptionDropped(
        SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken);
}