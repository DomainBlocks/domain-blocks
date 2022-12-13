using System;
using DomainBlocks.Projections.New;

namespace DomainBlocks.Projections.SqlStreamStore;

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
                var streamStore = sqlStreamStoreOptions.StreamStoreFactory();
                var eventPublisher = new SqlStreamStoreEventPublisher(streamStore);
                var eventDeserializer = sqlStreamStoreOptions.EventDeserializerFactory();

                var eventDispatcher = new EventDispatcher<StreamMessageWrapper, object>(
                    eventPublisher,
                    registry.EventProjectionMap,
                    registry.ProjectionContextMap,
                    eventDeserializer,
                    registry.EventNameMap,
                    EventDispatcherConfiguration.ReadModelDefaults);

                return eventDispatcher;
            });

        return optionsBuilder;
    }
}