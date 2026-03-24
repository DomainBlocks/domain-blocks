using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IFastEventAppender
{
    Task<AppendBatchResult> AppendBatchAsync(
        IEnumerable<BsonDocument> requests,
        CancellationToken cancellationToken = default);
}