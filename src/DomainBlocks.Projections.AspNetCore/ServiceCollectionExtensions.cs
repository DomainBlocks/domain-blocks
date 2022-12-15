using System;
using DomainBlocks.Projections.Builders;
using DomainBlocks.Projections.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHostedEventCatchUpSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventCatchUpSubscriptionOptionsBuilder, ProjectionModelBuilder> optionsAction)
    {
        return serviceCollection
            .AddEventCatchUpSubscription(optionsAction)
            .AddHostedService<EventDispatcherHostedService>();
    }
}