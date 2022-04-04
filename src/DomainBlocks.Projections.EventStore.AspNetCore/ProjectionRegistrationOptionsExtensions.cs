using DomainBlocks.EventStore.Common.AspNetCore;
using DomainBlocks.Projections.AspNetCore;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.EventStore.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static ProjectionRegistrationOptionsBuilder<object> UseEventStorePublishedEvents(
            this ProjectionRegistrationOptionsBuilder builder)
        {
            return builder.UseEventStorePublishedEvents<object>();
        }

        public static ProjectionRegistrationOptionsBuilder<TEventBase> UseEventStorePublishedEvents<TEventBase>(
            this ProjectionRegistrationOptionsBuilder builder)
        {
            builder.ServiceCollection.AddEventStore(builder.Configuration);
            var builderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure)builder;

            var typedBuilder = builderInfrastructure.TypedAs<TEventBase>();
            var typedBuilderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase>)typedBuilder;

            typedBuilderInfrastructure.UseEventPublisher(provider =>
            {
                var client = provider.GetRequiredService<EventStoreClient>();
                var notificationFactory = provider.GetRequiredService<EventStoreEventNotificationFactory>();
                return new EventStoreEventPublisher<TEventBase>(client, notificationFactory);
            });

            return typedBuilder;
        }

        public static ProjectionRegistrationOptionsBuilder<TEventBase> UseJsonDeserialization<TEventBase>(
            this ProjectionRegistrationOptionsBuilder<TEventBase> builder)
        {
            var builderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure<ResolvedEvent>)builder;
            builderInfrastructure.UseEventDeserializer(_ => new EventRecordJsonDeserializer());
            return builder;
        }
    }
}
