using System.Text.Json;
using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.Serialization;
using DomainBlocks.SqlStreamStore.Common.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.AspNetCore;

public static class ProjectionRegistrationOptionsExtensions
{
    public static ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> UseSqlStreamStorePublishedEvents(
        this ProjectionRegistrationOptionsBuilder builder)
    {
        builder.ServiceCollection.AddPostgresSqlStreamStore(builder.Configuration);
        var builderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure)builder;

        var typedBuilder = builderInfrastructure.TypedAs<StreamMessageWrapper>();
        var typedBuilderInfrastructure = (IProjectionRegistrationOptionsBuilderInfrastructure<StreamMessageWrapper>)typedBuilder;

        typedBuilderInfrastructure.UseEventPublisher(provider =>
        {
            var streamStore = provider.GetRequiredService<IStreamStore>();
            return new SqlStreamStoreEventPublisher(streamStore);
        });

        // Default to JSON deserialization unless overridden
        typedBuilder.UseJsonDeserialization();

        return typedBuilder;
    }

#nullable enable
    public static ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> UseJsonDeserialization(
        this ProjectionRegistrationOptionsBuilder<StreamMessageWrapper> builder,
        JsonSerializerOptions? serializerOptions = null)
    {
        var builderInfrastructure =
            (IProjectionRegistrationOptionsBuilderInfrastructure<StreamMessageWrapper>)builder;
        builderInfrastructure.UseEventDeserializer(_ => new StreamMessageJsonDeserializer(serializerOptions));
        return builder;
    }
#nullable restore
    
    // TODO (DS): This needs a bit more thought. Temporary until we refactor the projection builders/wire-up.
    public static IServiceCollection UseSqlStreamStorePublishedEvents(
        this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        serviceCollection.AddPostgresSqlStreamStore(configuration);

        serviceCollection.AddSingleton<IEventPublisher<StreamMessageWrapper>>(sp =>
        {
            var streamStore = sp.GetRequiredService<IStreamStore>();
            return new SqlStreamStoreEventPublisher(streamStore);
        });

        serviceCollection
            .AddTransient<IEventDeserializer<StreamMessageWrapper>>(_ => new StreamMessageJsonDeserializer());

        serviceCollection.AddHostedService(sp =>
        {
            var eventPublisher = sp.GetRequiredService<IEventPublisher<StreamMessageWrapper>>();
            var eventDeserializer = sp.GetRequiredService<IEventDeserializer<StreamMessageWrapper>>();
            var projectionRegistry = sp.GetRequiredService<ProjectionRegistry>();
            
            return new EventDispatcherHostedServiceNew<StreamMessageWrapper, object>(
                eventPublisher, eventDeserializer, projectionRegistry);
        });
        
        return serviceCollection;
    }
}