using System;
using DomainLib.EventStore.Common.AspNetCore;
using DomainLib.Projections.AspNetCore;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.EventStore.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStorePublishedEvents(
            this IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
        {
            builder.ServiceCollection.AddEventStore(builder.Configuration);

            builder.AddEventPublisher(() =>
            {
                var eventStoreClient = builder.ServiceProvider.GetRequiredService<EventStoreClient>();
                return new EventStoreEventPublisher(eventStoreClient);
            });
            return builder;
        }
    }
}
