namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal interface IRefCountedEventLogFeed
{
    Task<IAsyncDisposable> AttachAsync(
        IEventLogObserver observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}
