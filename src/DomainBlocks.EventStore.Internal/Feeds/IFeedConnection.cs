namespace DomainBlocks.EventStore.Internal.Feeds;

internal interface IFeedConnection<out THighWaterMark> : IAsyncDisposable
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
    THighWaterMark HighWaterMark { get; }
}