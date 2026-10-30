namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// A live feed of items that fans out to any number of observers. Disposing it stops the feed, and observers still
/// attached receive an <see cref="ObjectDisposedException"/>.
/// </summary>
internal interface IEventLogFeed<out T, out TMark> : IAsyncDisposable
{
    /// <summary>
    /// Gets a task that completes when the feed stops: successfully when disposed, or faulted when it fails
    /// irrecoverably.
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// Gets a mark at or after every item that the feed has delivered. The feed can deliver an item before reads see
    /// it, so a catch-up waits until reads reach the mark. What the mark is depends on the store.
    /// </summary>
    TMark HighWaterMark { get; }

    IDisposable Attach(IFeedObserver<T> observer, string correlationId = "unknown");

    Task ConnectAsync(CancellationToken cancellationToken = default);
}