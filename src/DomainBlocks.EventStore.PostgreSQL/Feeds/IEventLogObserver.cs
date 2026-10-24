namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Receives items from a live feed. Implementations must not block, as the feed pumps to every observer in turn.
/// </summary>
internal interface IEventLogObserver<in T>
{
    ValueTask OnNextAsync(T item, CancellationToken cancellationToken);

    /// <summary>
    /// Called after the feed reconnects. Rows committed while the feed was down were not delivered, so the observer
    /// must recover them from its last known position.
    /// </summary>
    ValueTask OnResetAsync(CancellationToken cancellationToken);

    ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken);
}