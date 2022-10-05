using System.Text.Json;
using DomainBlocks.EventStore.Common.AspNetCore;
using DomainBlocks.Projections.AspNetCore;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.EventStore.AspNetCore;

public static class ProjectionRegistrationOptionsExtensions
{
    public static ProjectionRegistrationOptionsBuilder<EventRecord> UseEventStorePublishedEvents(
        this ProjectionRegistrationOptionsBuilder builder)
    {
        builder.ServiceCollection.AddEventStore(builder.Configuration);
        var builderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure)builder;

        var typedBuilder = builderInfrastructure.TypedAs<EventRecord>();
        var typedBuilderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure<EventRecord>)typedBuilder;

        typedBuilderInfrastructure.UseEventPublisher(provider =>
        {
            var streamStore = provider.GetRequiredService<EventStoreClient>();
            return new EventStoreEventPublisher(streamStore);
        });

        return typedBuilder;
    }

    public static ProjectionRegistrationOptionsBuilder<EventRecord> UseJsonDeserialization(
        this ProjectionRegistrationOptionsBuilder<EventRecord> builder, JsonSerializerOptions serializerOptions = null)
    {
        var builderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure<EventRecord>)builder;
        builderInfrastructure.UseEventDeserializer(_ => new EventRecordJsonDeserializer(serializerOptions));
        return builder;
    }
}