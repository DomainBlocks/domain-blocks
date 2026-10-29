using DomainBlocks.EventStore.Internal.Feeds;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal interface IRefCountedEventLogFeed<out T>
{
    Task<IEventLogFeedAttachment> AttachAsync(
        IFeedObserver<T> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}