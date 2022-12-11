using System;
using DomainBlocks.Projections.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventCatchUpSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventCatchUpSubscriptionOptionsBuilder, ProjectionModelBuilder> optionsAction)
    {
        serviceCollection.AddSingleton(sp =>
        {
            var optionsBuilder = new EventCatchUpSubscriptionOptionsBuilder();
            var modelBuilder = new ProjectionModelBuilder();
            optionsAction(sp, optionsBuilder, modelBuilder);
            var options = optionsBuilder.Options;
            var model = modelBuilder.Build();
            return options.CreateEventDispatcher(model);
        });

        return serviceCollection;
    }
}