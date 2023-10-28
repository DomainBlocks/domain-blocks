namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamConsumer<in TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    /// <summary>
    /// Gets the checkpoint frequency to use while catching up.
    /// </summary>
    CheckpointFrequency CatchUpCheckpointFrequency => CheckpointFrequency.Default;

    /// <summary>
    /// Gets the checkpoint frequency to use when processing live events.
    /// </summary>
    CheckpointFrequency LiveCheckpointFrequency => CheckpointFrequency.Default;

    /// <summary>
    /// Invoked when the subscription is starting. This method is called once for the lifetime of a subscription.
    /// </summary>
    /// <returns>The position (exclusive) to subscribe from.</returns>
    Task<TPosition?> OnStarting(CancellationToken cancellationToken);

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
    /// Invoked whenever the number of processed events since the last checkpoint is equal to the checkpoint size. Also
    /// invoked when the subscription becomes live, or when the subscription is dropped.
    /// </summary>
    Task OnCheckpoint(TPosition position, CancellationToken cancellationToken);

    /// <summary>
    /// Invoked once all historical events have been processed.
    /// </summary>
    Task OnLive(CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when an exception is thrown from <see cref="OnEvent"/>, giving the consumer an opportunity to resolve
    /// the error.
    /// </summary>
    /// <returns>
    /// The resolution to apply.
    /// </returns>
    Task<EventErrorResolution> OnEventError(
        TEvent @event,
        TPosition position,
        Exception exception,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when the subscription is dropped, either due to a server error, client error, or because the
    /// subscription was disposed.
    /// </summary>
    Task OnSubscriptionDropped(
        SubscriptionDroppedReason reason,
        Exception? exception,
        CancellationToken cancellationToken);
}