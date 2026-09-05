namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamObserver<in TDocument>
{
    ValueTask OnNextAsync(TDocument change, CancellationToken cancellationToken);

    ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken);
}