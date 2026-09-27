namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IRefCountedChangeStreamSubject<out TChange>
{
    Task<IChangeStreamAttachment> AttachAsync(
        IChangeStreamObserver<TChange> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}