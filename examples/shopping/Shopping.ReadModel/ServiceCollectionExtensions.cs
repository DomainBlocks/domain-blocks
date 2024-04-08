using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.EventStoreDb;
using DomainBlocks.V1.Persistence.Builders;
using DomainBlocks.V1.SqlStreamStore;
using DomainBlocks.V1.Subscriptions;
using EventStore.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Domain.Events;

namespace Shopping.ReadModel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShoppingReadModel(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ShoppingCartDbContext>(
            options => options.UseNpgsql(configuration.GetConnectionString("ReadModel")),
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddDbContextFactory<ShoppingCartDbContext>();

        services.AddSingleton<IEventStreamConsumer, PostgresShoppingCartProjection>();

        var eventStoreType = configuration.GetValue<string>("EventStoreType")!;
        var connectionString = configuration.GetConnectionString(eventStoreType)!;
        IEventStore eventStore = null!;

        switch (eventStoreType)
        {
            case "EventStoreDb":
                var eventStoreDbSettings = EventStoreClientSettings.Create(connectionString);
                var eventStoreClient = new EventStoreClient(eventStoreDbSettings);
                eventStore = new EventStoreDbEventStore(eventStoreClient);
                break;
            case "SqlStreamStore":
                var streamStoreSettings = new PostgresStreamStoreSettings(connectionString);
                var streamStore = new PostgresStreamStore(streamStoreSettings);
                eventStore = new SqlStreamStoreEventStore(streamStore);
                break;
        }

        services.AddSingleton<EventStreamSubscriptionService>(sp =>
        {
            var consumers = sp.GetServices<IEventStreamConsumer>();
            var eventMapper = new EventMapperBuilder().MapAll<IDomainEvent>(_ => { }).Build();

            return new EventStreamSubscriptionService(
                "all-events",
                pos => eventStore.SubscribeToAll(pos == null ? null : new GlobalPosition(pos.Value.Value)),
                consumers,
                eventMapper);
        });

        services.AddHostedService<EventStreamSubscriptionHostedService>();

        return services;
    }
}