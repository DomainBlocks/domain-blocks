namespace DomainBlocks.EventStore.Internal.Feeds;

internal interface IRefCountedFeed<out T, TMark>
{
    Task<IFeedAttachment<TMark>> AttachAsync(
        IFeedObserver<T> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}