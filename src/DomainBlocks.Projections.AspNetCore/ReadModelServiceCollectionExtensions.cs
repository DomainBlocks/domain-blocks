using System;
using DomainBlocks.Projections.New.Builders;
using DomainBlocks.Projections.New.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore;

public static class ReadModelServiceCollectionExtensions
{
    public static IServiceCollection AddReadModel(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ProjectionRegistrationOptionsBuilder> buildProjectionOptions)
    {
        return services.AddReadModel<object>(configuration, buildProjectionOptions);
    }

    public static IServiceCollection AddReadModel<TEventBase>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ProjectionRegistrationOptionsBuilder> buildProjectionOptions)
    {
        var optionsBuilder = new ProjectionRegistrationOptionsBuilder(services, configuration);
        buildProjectionOptions(optionsBuilder);

        var rawDataType = optionsBuilder.RawDataType;
        var typedBuildMethod =
            typeof(IProjectionRegistrationOptionsBuilderInfrastructure)
                .GetMethod(nameof(IProjectionRegistrationOptionsBuilderInfrastructure.Build))
                ?.MakeGenericMethod(rawDataType);

        services.AddHostedService(provider =>
        {
            dynamic projectionOptions = typedBuildMethod?.Invoke(optionsBuilder, new[] { provider as object });

            if (projectionOptions == null)
            {
                throw new InvalidOperationException("Unable to build projection registrations");
            }

            var typedEventDispatcherHostedService = typeof(EventDispatcherHostedService<,>)
                .MakeGenericType(rawDataType, typeof(TEventBase));

            var constructorMethod = typedEventDispatcherHostedService.GetConstructor(new Type[]
            {
                typeof(ProjectionRegistryBuilder),
                projectionOptions.EventPublisher.GetType(),
                projectionOptions.EventSerializer.GetType(),
                projectionOptions.OnRegisteringProjections.GetType()
            });

            var eventDispatcher = (IEventDispatcherHostedService)constructorMethod?.Invoke(new object[]
            {
                new ProjectionRegistryBuilder(),
                projectionOptions.EventPublisher,
                projectionOptions.EventSerializer,
                projectionOptions.OnRegisteringProjections
            });

            return eventDispatcher;
        });

        return services;
    }
    
    public static IServiceCollection AddHostedEventSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventSubscriptionOptionsBuilder> optionsAction)
    {
        return serviceCollection
            .AddEventSubscription(optionsAction)
            .AddHostedService<EventDispatcherHostedServiceNew>();
    }
}