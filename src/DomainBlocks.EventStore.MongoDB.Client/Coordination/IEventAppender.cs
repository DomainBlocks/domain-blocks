using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IEventAppender
{
    void StartPrefetch(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken);

    Task<WriteResult> FlushAsync(CancellationToken cancellationToken);
}