using System;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.New;

public static class ProjectionsServiceCollectionExtensions
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