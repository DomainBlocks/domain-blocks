using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.Experimental.Persistence.Events;
using EventStore.Client;

namespace DomainBlocks.Experimental.Persistence.EventStoreDb;

public static class EventStoreDbEntityStoreBuilderExtensions
{
    public static EntityStoreBuilder UseEventStoreDb(this EntityStoreBuilder builder, string connectionString)
    {
        var settings = EventStoreClientSettings.Create(connectionString);
        return builder.UseEventStoreDb(settings);
    }

    public static EntityStoreBuilder UseEventStoreDb(this EntityStoreBuilder builder, EventStoreClientSettings settings)
    {
        var client = new EventStoreClient(settings);
        var eventStore = new EventStoreDbEventStore(client);
        var writeEventFactory = new BytesWriteEventFactory();
        return builder.SetInfrastructure(eventStore, writeEventFactory);
    }
}