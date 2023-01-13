using DomainBlocks.Core.Projections;
using DomainBlocks.Core.Projections.Builders;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using DomainBlocks.SqlStreamStore.Serialization;

namespace DomainBlocks.SqlStreamStore.Projections;

public static class EventSubscriptionOptionsBuilderExtensions
{
    public static EventCatchUpSubscriptionOptionsBuilder UseSqlStreamStore(
        this EventCatchUpSubscriptionOptionsBuilder optionsBuilder, Action<SqlStreamStoreOptionsBuilder> optionsAction)
    {
        var sqlStreamStoreOptionsBuilder = new SqlStreamStoreOptionsBuilder();
        optionsAction(sqlStreamStoreOptionsBuilder);
        var sqlStreamStoreOptions = sqlStreamStoreOptionsBuilder.Options;

        ((IEventCatchUpSubscriptionOptionsBuilderInfrastructure)optionsBuilder)
            .WithEventDispatcherFactory(registry =>
            {
                var streamStore = sqlStreamStoreOptions.GetOrCreateStreamStore();
                var eventPublisher = new SqlStreamStoreEventPublisher(streamStore);
                var eventSerializer = sqlStreamStoreOptions.GetEventDataSerializer();
                var eventConverter = new SqlStreamStoreEventAdapter(eventSerializer);

                var eventDispatcher = new EventDispatcher<StreamMessage>(
                    eventPublisher,
                    registry.EventProjectionMap,
                    registry.ProjectionContextMap,
                    eventConverter,
                    registry.EventNameMap,
                    EventDispatcherConfiguration.ReadModelDefaults);

                return eventDispatcher;
            });

        return optionsBuilder;
    }
}