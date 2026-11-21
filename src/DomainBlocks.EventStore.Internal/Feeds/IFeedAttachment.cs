namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// An observer's attachment to a feed. Disposing it detaches the observer.
/// </summary>
internal interface IFeedAttachment<TMark> : IAsyncDisposable
{
    /// <summary>
    /// Waits until the feed has connected, and returns its high-water mark, which covers every item delivered before
    /// the observer could receive it. The task faults if the feed stops before it connects.
    /// </summary>
    Task<TMark> WaitForConnectionAsync(CancellationToken cancellationToken);
}