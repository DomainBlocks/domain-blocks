using System;
using DomainBlocks.Projections.New.Builders;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.New.Extensions;

public static class EventSubscriptionOptionsBuilderExtensions
{
    public static EventSubscriptionOptionsBuilder UseSqlStreamStore(
        this EventSubscriptionOptionsBuilder optionsBuilder, string connectionString)
    {
        optionsBuilder.WithEventDispatcher(projections =>
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
                    ProjectionHandlerTimeout = TimeSpan.FromMinutes(1)
                });

            return eventDispatcher;
        });

        return optionsBuilder;
    }
}