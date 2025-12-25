using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore;

public interface IEventStoreClient : IAsyncDisposable
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<object>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<CommittedEvent<object>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}