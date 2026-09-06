namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamSubject<out TDocument>
{
    IDisposable Attach(IChangeStreamObserver<TDocument> observer, string correlationId = "unknown");

    IChangeStreamConnection Connect();
}