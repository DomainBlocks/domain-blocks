using DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.SqlStreamStore.Experimental;

public static class EventSourcedEntityStoreBuilderExtensions
{
    // TODO: support builder to specify postgres, etc.
    public static EntityStoreBuilder UseSqlStreamStore(
        this EntityStoreBuilder builder,
        IStreamStore streamStore,
        Action<EntityStoreOptionsBuilder<string>>? builderAction = null)
    {
        var optionsBuilder = new EntityStoreOptionsBuilder<string>();
        builderAction?.Invoke(optionsBuilder);
        var options = optionsBuilder.Build();

        var eventStore = new SqlStreamStoreEventStore(streamStore);
        var eventAdapter = new SqlStreamStoreEventAdapter();

        return builder.SetInfrastructure(eventStore, eventAdapter, options);
    }
}