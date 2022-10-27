using System;
using DomainBlocks.Projections.New;

namespace DomainBlocks.Projections.SqlStreamStore.New;

public static class EventSubscriptionOptionsBuilderExtensions
{
    public static void UseSqlStreamStore(
        this EventCatchUpSubscriptionOptionsBuilder optionsBuilder, Action<SqlStreamStoreOptionsBuilder> optionsAction)
    {
        var sqlStreamStoreOptionsBuilder = new SqlStreamStoreOptionsBuilder();
        optionsAction(sqlStreamStoreOptionsBuilder);
        var sqlStreamStoreOptions = sqlStreamStoreOptionsBuilder.Options;

        optionsBuilder.WithEventDispatcherFactory(projections =>
        {
            var streamStore = sqlStreamStoreOptions.StreamStoreFactory();
            var eventPublisher = new SqlStreamStoreEventPublisher(streamStore);
            var eventDeserializer = sqlStreamStoreOptions.EventDeserializerFactory();

            var eventDispatcher = new EventDispatcher<StreamMessageWrapper, object>(
                eventPublisher,
                projections.EventProjectionMap,
                projections.ProjectionContextMap,
                eventDeserializer,
                projections.EventNameMap,
                EventDispatcherConfiguration.ReadModelDefaults);

            return eventDispatcher;
        });
    }
}