namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public interface IChangeStreamObserver<in TDocument>
{
    ValueTask OnNextAsync(TDocument change, CancellationToken cancellationToken);
}