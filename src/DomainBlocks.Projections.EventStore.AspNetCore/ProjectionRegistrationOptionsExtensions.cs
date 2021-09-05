using System;
using DomainBlocks.EventStore.Common.AspNetCore;
using DomainBlocks.Projections.AspNetCore;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.EventStore.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStorePublishedEvents(
            this IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
        {
            builder.ServiceCollection.AddEventStore(builder.Configuration);

            builder.AddEventPublisher(provider =>
            {
                var eventStoreClient = provider.GetRequiredService<EventStoreClient>();
                return new EventStoreEventPublisher(eventStoreClient);
            });
            return builder;
        }
    }
}
