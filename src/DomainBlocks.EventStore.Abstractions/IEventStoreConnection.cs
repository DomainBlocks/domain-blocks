using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreConnection<TEventData, TMetadata> where TEventData : notnull where TMetadata : notnull
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEventData, TMetadata>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent<TEventData, TMetadata>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}