using DomainBlocks.V1.Persistence.Builders;
using EventStore.Client;

namespace DomainBlocks.V1.EventStoreDb.Extensions;

public static class EntityStoreBuilderExtensions
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