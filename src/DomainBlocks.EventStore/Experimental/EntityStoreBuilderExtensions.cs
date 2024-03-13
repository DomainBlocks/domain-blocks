using DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;
using EventStore.Client;

namespace DomainBlocks.EventStore.Experimental;

public static class EntityStoreBuilderExtensions
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
        var optionsBuilder = new EntityStoreConfigBuilder<ReadOnlyMemory<byte>>();
        builderAction?.Invoke(optionsBuilder);
        var options = optionsBuilder.Build();

        var client = new EventStoreClient(settings);
        var eventStore = new EventStoreDbEventStore(client);
        var eventAdapter = new EventStoreDbEventAdapter();

        return builder.SetInfrastructure(eventStore, eventAdapter, options);
    }
}