using DomainBlocks.Core.Builders;
using DomainBlocks.Core.Persistence;
using DomainBlocks.Core.Projections.Builders;
using DomainBlocks.Core.Subscriptions.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAggregateRepository(
        this IServiceCollection services,
        Action<IServiceProvider, AggregateRepositoryOptionsBuilder, ModelBuilder> optionsAction)
    {
        return services.AddSingleton(sp =>
        {
            var optionsBuilder = new AggregateRepositoryOptionsBuilder();
            var modelBuilder = new ModelBuilder();
            optionsAction(sp, optionsBuilder, modelBuilder);
            var options = optionsBuilder.Options;
            var model = modelBuilder.Build();

            return options.CreateAggregateRepository(model);
        });
    }

    public static IServiceCollection AddAggregateRepository(
        this IServiceCollection services,
        Action<AggregateRepositoryOptionsBuilder, ModelBuilder> optionsAction)
    {
        return services.AddAggregateRepository((_, options, model) => optionsAction(options, model));
    }

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

    public static IServiceCollection AddEventStreamSubscription(
        this IServiceCollection services,
        Action<EventStreamSubscriptionBuilder> builderAction) =>
        services.AddEventStreamSubscription((_, options) => builderAction(options));

    public static IServiceCollection AddEventStreamSubscription(
        this IServiceCollection services,
        Action<IServiceProvider, EventStreamSubscriptionBuilder> builderAction)
    {
        services.AddSingleton(sp =>
        {
            var builder = new EventStreamSubscriptionBuilder();
            builderAction(sp, builder);
            return builder.Build();
        });

        return services;
    }
}