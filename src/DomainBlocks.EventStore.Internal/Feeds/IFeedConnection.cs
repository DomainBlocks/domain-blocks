namespace DomainBlocks.EventStore.Internal.Feeds;

internal interface IFeedConnection<out THighWaterMark> : IAsyncDisposable
{
    /// <summary>
    /// Completes when the feed stops: successfully when disposed, or faulted when it fails irrecoverably.
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// A mark at or after every item that the feed has delivered. The feed can deliver an item before reads see it, so
    /// a catch-up waits until reads reach the mark. What the mark is depends on the store.
    /// </summary>
    THighWaterMark HighWaterMark { get; }
}