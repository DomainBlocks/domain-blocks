using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IEventLogAppender
{
    void StartPrefetch(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken);

    Task<AppendBatchResult> FlushAsync(CancellationToken cancellationToken);
}