namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IRefCountedChangeStreamSubject<out TDocument>
{
    Task<IChangeStreamAttachment> AttachAsync(
        IChangeStreamObserver<TDocument> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default);
}