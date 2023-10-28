namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamConsumer<in TEvent, TPosition> : IEventStreamListener<TEvent, TPosition>
    where TPosition : struct
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
    /// Invoked whenever the number of processed events since the last checkpoint is equal to the checkpoint size. Also
    /// invoked when the subscription becomes live, or when the subscription is dropped.
    /// </summary>
    Task OnCheckpoint(TPosition position, CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when an exception is thrown from <see cref="IEventStreamListener{TEvent,TPosition}.OnEvent"/>, giving
    /// the consumer an opportunity to resolve the error.
    /// </summary>
    /// <returns>
    /// The resolution to apply.
    /// </returns>
    Task<EventErrorResolution> OnEventError(
        TEvent @event, TPosition position, Exception exception, CancellationToken cancellationToken);
}