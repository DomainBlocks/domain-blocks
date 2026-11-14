namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Receives items from a live feed. Implementations must not block, as the feed pumps to every observer in turn.
/// </summary>
internal interface IFeedObserver<in T>
{
    ValueTask OnNextAsync(T item, CancellationToken cancellationToken);

    /// <summary>
    /// Called after the feed reconnects without resuming. Items committed while the feed was down were not delivered,
    /// so the observer must recover them from its last known position.
    /// </summary>
    ValueTask OnResetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Called after each transient failure, which the feed will retry. The feed may recover, so this doesn't end it.
    /// </summary>
    ValueTask OnFailureAsync(FeedFailure failure, CancellationToken cancellationToken);

    ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken);
}