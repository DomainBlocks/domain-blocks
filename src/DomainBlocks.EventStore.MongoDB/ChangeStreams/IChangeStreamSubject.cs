namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamSubject<out TChange>
{
    IDisposable Attach(IChangeStreamObserver<TChange> observer, string correlationId = "unknown");

    Task<IChangeStreamConnection> ConnectAsync(CancellationToken cancellationToken = default);
}