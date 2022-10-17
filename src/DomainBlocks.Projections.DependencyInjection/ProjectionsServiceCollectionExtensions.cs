using System;
using DomainBlocks.Projections.New.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.DependencyInjection;

public static class ProjectionsServiceCollectionExtensions
{
    public static IServiceCollection AddEventSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventSubscriptionOptionsBuilder> optionsAction)
    {
        serviceCollection.AddSingleton(sp =>
        {
            var optionsBuilder = new EventSubscriptionOptionsBuilder();
            optionsAction(sp, optionsBuilder);
            var options = optionsBuilder.Build();
            return options.EventDispatcher;
        });

        return serviceCollection;
    }
}