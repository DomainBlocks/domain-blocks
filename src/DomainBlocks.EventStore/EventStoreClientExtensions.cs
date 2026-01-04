using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventStoreClientExtensions
{
    public static Task AppendToStreamAsync<TEvent>(
        this IEventStoreClient<TEvent> client,
        string streamId,
        IEnumerable<TEvent> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default) where TEvent : notnull
    {
        return client.AppendToStreamAsync(
            streamId,
            events.Select(x => AppendEvent.Create(x)),
            options,
            cancellationToken);
    }
}