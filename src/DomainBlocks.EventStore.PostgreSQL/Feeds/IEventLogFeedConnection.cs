namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal interface IEventLogFeedConnection : IAsyncDisposable
{
    /// <summary>
    /// Completes when the feed stops: successfully when disposed, or faulted when it fails irrecoverably.
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// The position of the last row the feed delivered, or <see langword="null"/> if it has delivered none. The feed
    /// can deliver a row before queries can see it, so a catch-up waits until they see this position.
    /// </summary>
    long? PositionHighWaterMark { get; }
}