using System;
using DomainBlocks.Projections.New.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.DependencyInjection;

public static class ProjectionsServiceCollectionExtensions
{
    public static IServiceCollection AddProjection<TState>(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, ProjectionOptionsBuilder<TState>> optionsAction)
    {
        serviceCollection.AddSingleton(sp =>
        {
            var optionsBuilder = new ProjectionOptionsBuilder<TState>();
            optionsAction(sp, optionsBuilder);
            return optionsBuilder.Build();
        });

        return serviceCollection;
    }
}