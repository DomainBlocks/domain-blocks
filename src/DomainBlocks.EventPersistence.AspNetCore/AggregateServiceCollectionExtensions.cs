using System;
using DomainBlocks.Core.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewAggregateRepositoryOptionsBuilder = DomainBlocks.Persistence.New.AggregateRepositoryOptionsBuilder;

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
        Action<ModelBuilder> modelBuilderAction)
    {
        var optionsBuilder = new AggregateRepositoryOptionsBuilder(services, configuration);
        optionsBuilderAction(optionsBuilder);

        var modelBuilder = new ModelBuilder();
        modelBuilderAction(modelBuilder);

        var rawDataType = optionsBuilder.RawDataType;
        var typedBuildMethod =
            typeof(IAggregateRepositoryOptionsBuilderInfrastructure)
                .GetMethod(nameof(IAggregateRepositoryOptionsBuilderInfrastructure.Build))
                ?.MakeGenericMethod(rawDataType);

        services.AddSingleton<IAggregateRepository>(provider =>
        {
            var model = modelBuilder.Build();

            dynamic aggregateRepositoryOptions = typedBuildMethod?.Invoke(
                optionsBuilder, new[] { provider, model.EventNameMap as object });

            if (aggregateRepositoryOptions == null)
            {
                throw new InvalidOperationException("Unable to build aggregation registrations");
            }

            return AggregateRepository.Create(
                aggregateRepositoryOptions.EventsRepository,
                aggregateRepositoryOptions.SnapshotRepository,
                model);
        });

        return services;
    }

    // New approach
    public static IServiceCollection AddAggregateRepository(
        this IServiceCollection services,
        Action<IServiceProvider, NewAggregateRepositoryOptionsBuilder, ModelBuilder> optionsAction)
    {
        return services.AddSingleton(sp =>
        {
            var optionsBuilder = new NewAggregateRepositoryOptionsBuilder();
            var modelBuilder = new ModelBuilder();
            optionsAction(sp, optionsBuilder, modelBuilder);
            var options = optionsBuilder.Options;
            var model = modelBuilder.Build();

            return options.CreateAggregateRepository(model);
        });
    }

    public static IServiceCollection AddAggregateRepository(
        this IServiceCollection services,
        Action<NewAggregateRepositoryOptionsBuilder, ModelBuilder> optionsAction)
    {
        return services.AddAggregateRepository((_, options, model) => optionsAction(options, model));
    }
}