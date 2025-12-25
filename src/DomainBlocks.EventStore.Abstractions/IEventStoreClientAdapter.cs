using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClientAdapter<TSerialized> where TSerialized : notnull
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TSerialized>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<CommittedEvent<TSerialized>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}