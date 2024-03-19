using DomainBlocks.Experimental.Persistence.Configuration;
using EventStore.Client;

namespace DomainBlocks.Experimental.Persistence.EventStoreDb;

public static class EventStoreDbEntityStoreBuilderExtensions
{
    public static EntityStoreBuilder UseEventStoreDb(
        this EntityStoreBuilder builder,
        string connectionString,
        Action<EntityStoreConfigBuilder<ReadOnlyMemory<byte>>>? builderAction = null)
    {
        var settings = EventStoreClientSettings.Create(connectionString);
        return builder.UseEventStoreDb(settings, builderAction);
    }

    public static EntityStoreBuilder UseEventStoreDb(
        this EntityStoreBuilder builder,
        EventStoreClientSettings settings,
        Action<EntityStoreConfigBuilder<ReadOnlyMemory<byte>>>? builderAction = null)
    {
        var dataConfigBuilder = new EntityStoreConfigBuilder<ReadOnlyMemory<byte>>();
        builderAction?.Invoke(dataConfigBuilder);
        var dataConfig = dataConfigBuilder.Build();

        var client = new EventStoreClient(settings);
        var eventStore = new EventStoreDbEventStore(client);
        return builder.SetInfrastructure(eventStore, dataConfig);
    }
}