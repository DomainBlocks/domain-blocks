using DomainBlocks.Experimental.EventSourcing.Persistence;
using EventStore.Client;

namespace DomainBlocks.EventStore.Experimental;

public static class EventSourcedStateRepositoryBuilderExtensions
{
    public static EventSourcedStateRepositoryBuilder<ReadOnlyMemory<byte>> UseEventStore(
        this EventSourcedStateRepositoryBuilder @this, string connectionString)
    {
        var settings = EventStoreClientSettings.Create(connectionString);
        return @this.UseEventStore(settings);
    }

    public static EventSourcedStateRepositoryBuilder<ReadOnlyMemory<byte>> UseEventStore(
        this EventSourcedStateRepositoryBuilder @this, EventStoreClientSettings settings)
    {
        var client = new EventStoreClient(settings);
        var eventStreamRepository = new EventStoreEventRepository(client);
        var eventAdapter = new EventStoreEventAdapter();

        return @this.Use(eventStreamRepository, eventAdapter);
    }
}