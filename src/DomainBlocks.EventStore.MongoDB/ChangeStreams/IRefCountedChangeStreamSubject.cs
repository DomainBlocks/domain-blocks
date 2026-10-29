using DomainBlocks.EventStore.Internal.Feeds;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IRefCountedChangeStreamSubject<out TChange>
{
    Task<IChangeStreamAttachment> AttachAsync(
        IFeedObserver<TChange> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}