using DomainBlocks.EventStore.MongoDB.Client.Schema2;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IEventAppender
{
    Task<WriteResult> AppendBatchAsync(
        IEnumerable<AppendRequest> requests,
        CancellationToken cancellationToken = default);
}