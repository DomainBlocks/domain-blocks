using DomainBlocks.Persistence.Builders;
using EventStore.Client;

namespace DomainBlocks.Persistence.EventStoreDb;

public static class EventStoreDbEntityStoreBuilderExtensions
{
    public static EntityStoreConfigBuilder UseEventStoreDb(
        this EntityStoreConfigBuilder builder, string connectionString)
    {
        var settings = EventStoreClientSettings.Create(connectionString);
        return builder.UseEventStoreDb(settings);
    }

    public static EntityStoreConfigBuilder UseEventStoreDb(
        this EntityStoreConfigBuilder builder, EventStoreClientSettings settings)
    {
        var client = new EventStoreClient(settings);
        var eventStore = new EventStoreDbEventStore(client);
        return builder.SetEventStore(eventStore);
    }
}