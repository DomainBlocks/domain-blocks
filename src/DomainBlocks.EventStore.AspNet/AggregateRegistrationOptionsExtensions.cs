using System;
using DomainBlocks.EventStore.Common.AspNetCore;
using DomainBlocks.Persistence.AspNetCore;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.EventStore.AspNetCore
{
    public static class AggregateRegistrationOptionsExtensions
    {
        public static IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStoreDbForEvents(
            this IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
        {
            builder.ServiceCollection.AddEventStore(builder.Configuration);

            builder.AddEventsRepository((provider, serializer) =>
            {
                var eventStoreClient = provider.GetRequiredService<EventStoreClient>();
                return new EventStoreEventsRepository(eventStoreClient, serializer);
            });

            return builder;
        }

        public static IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStoreDbForSnapshots(
            this IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
        {
            builder.ServiceCollection.AddEventStore(builder.Configuration);

            builder.AddSnapshotRepository((provider, serializer) =>
            {
                var eventStoreClient = provider.GetRequiredService<EventStoreClient>();
                return new EventStoreSnapshotRepository(eventStoreClient, serializer);
            });

            return builder;
        }

        public static IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStoreDbForEventsAndSnapshots(
            this IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
        {
            builder.ServiceCollection.AddEventStore(builder.Configuration);

            builder.AddEventsRepository((provider, serializer) =>
            {
                var eventStoreClient = provider.GetRequiredService<EventStoreClient>();
                return new EventStoreEventsRepository(eventStoreClient, serializer);
            });

            builder.AddSnapshotRepository((provider, serializer) =>
            {
                var eventStoreClient = provider.GetRequiredService<EventStoreClient>();
                return new EventStoreSnapshotRepository(eventStoreClient, serializer);
            });

            return builder;
        }
    }
}