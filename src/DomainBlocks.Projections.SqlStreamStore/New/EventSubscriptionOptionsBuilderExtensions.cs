using System.Text.Json;
using DomainBlocks.Projections.New;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.New;

public static class EventSubscriptionOptionsBuilderExtensions
{
    public static EventCatchUpSubscriptionOptionsBuilder UseSqlStreamStore(
        this EventCatchUpSubscriptionOptionsBuilder optionsBuilder,
        PostgresStreamStoreSettings settings,
        // TODO (DS): Allow serialization options to be specified as part of builder options for SQLStreamStore.
        JsonSerializerOptions jsonSerializerOptions = null)
    {
        optionsBuilder.WithEventDispatcherFactory(projections =>
        {
            // TODO (DS): Don't directly reference SqlStreamStore.Postgres in this assembly. We need proper options
            // which allow us to select which underlying infrastructure to use. Address in a future PR.
            var streamStore = new PostgresStreamStore(settings);
            streamStore.CreateSchemaIfNotExists().Wait();

            var eventPublisher = new SqlStreamStoreEventPublisher(streamStore);
            var eventDeserializer = new StreamMessageJsonDeserializer(jsonSerializerOptions);

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