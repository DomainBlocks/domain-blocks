using System;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.New;

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
            return optionsBuilder.Options.CreateEventDispatcher();
        });

        return serviceCollection;
    }
}