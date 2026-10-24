namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// An observer's attachment to a feed. Disposing it detaches the observer.
/// </summary>
internal interface IEventLogFeedAttachment : IAsyncDisposable
{
    /// <summary>
    /// The connection's <see cref="IEventLogFeedConnection.PositionHighWaterMark"/>, read once the observer was
    /// attached, so it covers every row delivered before the observer could receive it.
    /// </summary>
    long? PositionHighWaterMark { get; }
}