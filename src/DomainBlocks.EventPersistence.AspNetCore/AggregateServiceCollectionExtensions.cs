using System;
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
        Action<AggregateRepositoryOptionsBuilder> optionsAction,
        AggregateRegistry<object, object> aggregateRegistry)
    {
        return AddAggregateRepository<object, object>(services, configuration, optionsAction, aggregateRegistry);
    }

    /// <summary>
    /// Adds an aggregate repository to persist events 
    /// </summary>
    public static IServiceCollection AddAggregateRepository<TCommandBase, TEventBase>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AggregateRepositoryOptionsBuilder> optionsAction,
        AggregateRegistry<TCommandBase, TEventBase> aggregateRegistry)
    {
        var optionsBuilder = new AggregateRepositoryOptionsBuilder(services, configuration);

        optionsAction(optionsBuilder);

        var rawDataType = optionsBuilder.RawDataType;
        var typedBuildMethod =
            typeof(IAggregateRepositoryOptionsBuilderInfrastructure)
                .GetMethod(nameof(IAggregateRepositoryOptionsBuilderInfrastructure.Build))
                ?.MakeGenericMethod(rawDataType);

        services.AddSingleton<IAggregateRepository<TCommandBase, TEventBase>>(provider =>
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

        services.AddSingleton(aggregateRegistry.CommandDispatcher);

        return services;
    }
}