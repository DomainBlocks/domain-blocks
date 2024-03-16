using DomainBlocks.Experimental.Persistence.Configuration;
using EventStore.Client;

namespace DomainBlocks.Experimental.Persistence.EventStoreDb;

public static class EntityStoreBuilderExtensions
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
        var eventAdapter = new EventStoreDbEventAdapter();

        return builder.SetInfrastructure(eventStore, eventAdapter);
    }
}