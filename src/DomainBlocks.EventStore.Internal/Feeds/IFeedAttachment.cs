namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// An observer's attachment to a feed. Disposing it detaches the observer.
/// </summary>
internal interface IFeedAttachment<out THighWaterMark> : IAsyncDisposable
{
    /// <summary>
    /// The connection's <see cref="IFeedConnection{THighWaterMark}.HighWaterMark"/>, read once the observer was
    /// attached, so it covers every item delivered before the observer could receive it.
    /// </summary>
    THighWaterMark HighWaterMark { get; }
}