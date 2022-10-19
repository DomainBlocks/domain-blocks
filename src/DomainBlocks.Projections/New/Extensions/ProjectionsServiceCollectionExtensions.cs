using System;
using DomainBlocks.Projections.New.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.New.Extensions;

public static class ProjectionsServiceCollectionExtensions
{
    public static IServiceCollection AddEventCatchUpSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventCatchUpSubscriptionOptionsBuilder> optionsAction)
    {
        serviceCollection.AddSingleton(sp =>
        {
            var optionsBuilder = new EventCatchUpSubscriptionOptionsBuilder();
            optionsAction(sp, optionsBuilder);
            var options = optionsBuilder.Build();
            return options.EventDispatcher;
        });

        return serviceCollection;
    }
}