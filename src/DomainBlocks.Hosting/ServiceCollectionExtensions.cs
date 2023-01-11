using System;
using DomainBlocks.Core.Projections.Builders;
using DomainBlocks.Core.Subscriptions.Builders;
using DomainBlocks.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Hosting;

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

    public static IServiceCollection AddHostedEventStreamSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventStreamSubscriptionBuilder> builderAction)
    {
        return serviceCollection
            .AddEventStreamSubscription(builderAction)
            .AddHostedService<EventStreamSubscriptionHostedService>();
    }
}