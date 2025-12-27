using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore;

public interface IEventStoreClient<TEventBase> : IAsyncDisposable where TEventBase : class
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TEventBase>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<CommittedEvent<TEventBase>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}