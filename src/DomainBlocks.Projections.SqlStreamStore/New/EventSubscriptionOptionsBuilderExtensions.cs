using System;
using DomainBlocks.Projections.New;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.New;

public static class EventSubscriptionOptionsBuilderExtensions
{
    public static EventCatchUpSubscriptionOptionsBuilder UseSqlStreamStore(
        this EventCatchUpSubscriptionOptionsBuilder optionsBuilder, string connectionString)
    {
        optionsBuilder.WithEventDispatcherFactory(projections =>
        {
            // TODO (DS): Should we be directly referencing postgres StreamStore from here?
            var settings = new PostgresStreamStoreSettings(connectionString);
            var streamStore = new PostgresStreamStore(settings);
            var eventPublisher = new SqlStreamStoreEventPublisher(streamStore);
            var eventDeserializer = new StreamMessageJsonDeserializer();

            var eventDispatcher = new EventDispatcher<StreamMessageWrapper, object>(
                eventPublisher,
                projections.EventProjectionMap,
                projections.ProjectionContextMap,
                eventDeserializer,
                projections.EventNameMap,
                EventDispatcherConfiguration.ReadModelDefaults with
                {
                    // TODO: Remove
                    ProjectionHandlerTimeout = TimeSpan.FromMinutes(1)
                });

            return eventDispatcher;
        });

        return optionsBuilder;
    }
}