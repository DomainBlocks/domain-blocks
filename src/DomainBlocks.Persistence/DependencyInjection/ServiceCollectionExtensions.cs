using System;
using DomainBlocks.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAggregateRepository(
        this IServiceCollection services,
        Action<IServiceProvider, AggregateRepositoryOptionsBuilder, ModelBuilder> optionsAction)
    {
        return services.AddSingleton(sp =>
        {
            var optionsBuilder = new AggregateRepositoryOptionsBuilder();
            var modelBuilder = new ModelBuilder();
            optionsAction(sp, optionsBuilder, modelBuilder);
            var options = optionsBuilder.Options;
            var model = modelBuilder.Build();

            return options.CreateAggregateRepository(model);
        });
    }

    public static IServiceCollection AddAggregateRepository(
        this IServiceCollection services,
        Action<AggregateRepositoryOptionsBuilder, ModelBuilder> optionsAction)
    {
        return services.AddAggregateRepository((_, options, model) => optionsAction(options, model));
    }
}