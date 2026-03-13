namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public interface IChangeStreamObserver<in TDocument>
{
    ValueTask OnNextAsync(TDocument document, CancellationToken cancellationToken = default);
}