using System;
using DomainBlocks.Projections.New;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventCatchUpSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventCatchUpSubscriptionOptionsBuilder> optionsAction)
    {
        serviceCollection.AddSingleton(sp =>
        {
            var optionsBuilder = new EventCatchUpSubscriptionOptionsBuilder();
            optionsAction(sp, optionsBuilder);
            return optionsBuilder.Options.CreateEventDispatcher();
        });

        return serviceCollection;
    }
}