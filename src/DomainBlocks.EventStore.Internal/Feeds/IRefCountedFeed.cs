namespace DomainBlocks.EventStore.Internal.Feeds;

internal interface IRefCountedFeed<out T, THighWaterMark>
{
    Task<IFeedAttachment<THighWaterMark>> AttachAsync(
        IFeedObserver<T> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}