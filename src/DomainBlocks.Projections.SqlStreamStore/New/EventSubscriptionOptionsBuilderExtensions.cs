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
            // TODO (DS): Don't directly reference SqlStreamStore.Postgres in this assembly. We need options which allow
            // us to select which underlying infrastructure to use. Address in a future PR.
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
                EventDispatcherConfiguration.ReadModelDefaults);

            return eventDispatcher;
        });

        return optionsBuilder;
    }
}