using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IFastEventAppender
{
    void StartPrefetch(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken);

    Task<WriteResult> FlushAsync(CancellationToken cancellationToken);
}