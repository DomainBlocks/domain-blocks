namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Receives items from a live event log feed. Implementations must not block: the feed pumps items to every observer
/// in turn, and a slow observer would stall the feed for all of them.
/// </summary>
internal interface IEventLogObserver<in T>
{
    ValueTask OnNextAsync(T item, CancellationToken cancellationToken);

    /// <summary>
    /// Called after the feed has re-established its session. Rows committed while the feed was down were not
    /// delivered, so the observer must recover them from its last known position.
    /// </summary>
    ValueTask OnResetAsync(CancellationToken cancellationToken);

    ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken);
}