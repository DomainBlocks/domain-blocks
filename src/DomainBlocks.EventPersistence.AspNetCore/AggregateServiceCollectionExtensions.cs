using System;
using DomainBlocks.Persistence.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.AspNetCore;

public static class AggregateServiceCollectionExtensions
{
    /// <summary>
    /// Adds an aggregate repository to persist events 
    /// </summary>
    public static IServiceCollection AddAggregateRepository(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AggregateRepositoryOptionsBuilder> optionsBuilderAction,
        Action<AggregateRegistryBuilder<object>> registryBuilderAction)
    {
        return AddAggregateRepository<object>(services, configuration, optionsBuilderAction, registryBuilderAction);
    }

    /// <summary>
    /// Adds an aggregate repository to persist events 
    /// </summary>
    public static IServiceCollection AddAggregateRepository<TEventBase>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AggregateRepositoryOptionsBuilder> optionsBuilderAction,
        Action<AggregateRegistryBuilder<TEventBase>> registryBuilderAction)
    {
        var optionsBuilder = new AggregateRepositoryOptionsBuilder(services, configuration);
        optionsBuilderAction(optionsBuilder);

        var registryBuilder = new AggregateRegistryBuilder<TEventBase>();
        registryBuilderAction(registryBuilder);
        var aggregateRegistry = registryBuilder.Build();

        var rawDataType = optionsBuilder.RawDataType;
        var typedBuildMethod =
            typeof(IAggregateRepositoryOptionsBuilderInfrastructure)
                .GetMethod(nameof(IAggregateRepositoryOptionsBuilderInfrastructure.Build))
                ?.MakeGenericMethod(rawDataType);

        services.AddSingleton<IAggregateRepository<TEventBase>>(provider =>
        {
            dynamic aggregateRepositoryOptions = typedBuildMethod?.Invoke(
                optionsBuilder, new[] { provider, aggregateRegistry.EventNameMap as object });

            if (aggregateRepositoryOptions == null)
            {
                throw new InvalidOperationException("Unable to build aggregation registrations");
            }

            return AggregateRepository.Create(
                aggregateRepositoryOptions.EventsRepository,
                aggregateRepositoryOptions.SnapshotRepository,
                aggregateRegistry);
        });

        return services;
    }
}