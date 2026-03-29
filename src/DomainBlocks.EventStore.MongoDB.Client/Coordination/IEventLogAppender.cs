using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IEventLogAppender
{
    Task<AppendBatchResult> AppendBatchAsync(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken);
}