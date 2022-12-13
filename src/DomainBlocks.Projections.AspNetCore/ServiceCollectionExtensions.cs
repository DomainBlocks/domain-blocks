using System;
using DomainBlocks.Projections.DependencyInjection;
using DomainBlocks.Projections.New;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHostedEventCatchUpSubscription(
        this IServiceCollection serviceCollection,
        Action<IServiceProvider, EventCatchUpSubscriptionOptionsBuilder> optionsAction)
    {
        return serviceCollection
            .AddEventCatchUpSubscription(optionsAction)
            .AddHostedService<EventDispatcherHostedService>();
    }
}