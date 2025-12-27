using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore;

public static class EventStoreClientExtensions
{
    public static Task AppendToStreamAsync<TEventBase>(
        this IEventStoreClient<TEventBase> client,
        string streamId,
        IEnumerable<TEventBase> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default) where TEventBase : class
    {
        return client.AppendToStreamAsync(streamId, events.Select(UncommittedEvent.Create), options, cancellationToken);
    }
}