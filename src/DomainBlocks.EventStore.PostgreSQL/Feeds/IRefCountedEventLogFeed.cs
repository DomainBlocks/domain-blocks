namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal interface IRefCountedEventLogFeed<out T>
{
    Task<IAsyncDisposable> AttachAsync(
        IEventLogObserver<T> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}