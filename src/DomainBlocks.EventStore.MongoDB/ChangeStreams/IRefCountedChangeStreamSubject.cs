namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IRefCountedChangeStreamSubject<out TDocument>
{
    IAsyncDisposable Attach(IChangeStreamObserver<TDocument> observer, string correlationId = "unknown");
}