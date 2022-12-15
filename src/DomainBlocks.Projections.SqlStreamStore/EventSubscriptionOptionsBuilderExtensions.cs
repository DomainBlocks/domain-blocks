using System;
using DomainBlocks.Core.Serialization.SqlStreamStore;
using DomainBlocks.SqlStreamStore;
using DomainBlocks.Projections.Builders;
using SqlStreamStore.Streams;

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