using DomainBlocks.EventStore.MongoDB.Client.Schema2;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IEventAppender
{
    Task<AppendBatchResult> AppendBatchAsync(
        IEnumerable<AppendRequest> requests,
        CancellationToken cancellationToken = default);
}