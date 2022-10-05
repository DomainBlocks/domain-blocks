using System;
using DomainBlocks.EventStore.Common.AspNetCore;
using DomainBlocks.Persistence.AspNetCore;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.EventStore.AspNetCore;

public static class AggregateRegistrationOptionsExtensions
{
    public static IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStoreDbForEvents(
        this IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
    {
        builder.ServiceCollection.AddEventStore(builder.Configuration);

        builder.AddEventsRepository((provider, serializer) =>
        {
            var eventStoreClient = provider.GetRequiredService<EventStoreClient>();
            return new EventStoreEventsRepository(eventStoreClient, serializer);
        });

        return builder;
    }

    public static IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStoreDbForSnapshots(
        this IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
    {
        builder.ServiceCollection.AddEventStore(builder.Configuration);

        builder.AddSnapshotRepository((provider, serializer) =>
        {
            var eventStoreClient = provider.GetRequiredService<EventStoreClient>();
            return new EventStoreSnapshotRepository(eventStoreClient, serializer);
        });

        return builder;
    }

    public static IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseEventStoreDbForEventsAndSnapshots(
        this IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
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