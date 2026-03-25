using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IFastEventAppender
{
    void StartPrefetch(IEnumerable<BsonDocument> requests, CancellationToken ct);

    Task<AppendBatchResult> FlushAsync(CancellationToken ct);
}