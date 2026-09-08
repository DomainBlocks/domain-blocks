namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal interface IEventLogFeedConnection : IAsyncDisposable
{
    /// <summary>
    /// Completes when the feed stops: successfully when disposed, or faulted when it fails irrecoverably.
    /// </summary>
    Task Completion { get; }
}
