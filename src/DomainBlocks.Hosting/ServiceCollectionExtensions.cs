using System;
using DomainBlocks.Core.Subscriptions.Builders;
using DomainBlocks.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHostedEventStreamSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventStreamSubscriptionBuilder> builderAction)
    {
        return serviceCollection
            .AddEventStreamSubscription(builderAction)
            .AddHostedService<EventStreamSubscriptionHostedService>();
    }
}