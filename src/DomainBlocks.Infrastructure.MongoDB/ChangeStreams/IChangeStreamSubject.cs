namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public interface IChangeStreamSubject<out TDocument>
{
    IDisposable Attach(IChangeStreamObserver<TDocument> observer);

    IAsyncDisposable ConnectAsync(CancellationToken cancellationToken = default);
}