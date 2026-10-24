namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamObserver<in TChange>
{
    ValueTask OnNextAsync(TChange change, CancellationToken cancellationToken);

    ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken);
}