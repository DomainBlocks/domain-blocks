using DomainBlocks.Persistence.DependencyInjection;
using DomainBlocks.Persistence.EventStoreDb;
using DomainBlocks.Persistence.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Domain.Events;

namespace Shopping.WriteModel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShoppingWriteModel(
        this IServiceCollection services, IConfiguration configuration)
    {
        var eventStore = configuration.GetValue<string>("EventStore");
        var connectionString = configuration.GetConnectionString(eventStore);

        services.AddEntityStore(config =>
        {
            config
                .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(AggregateAdapter<>)))
                .MapEvents(x => x.MapAll<IDomainEvent>());

            switch (eventStore)
            {
                case "EventStoreDb":
                    config.UseEventStoreDb(connectionString);
                    break;
                case "SqlStreamStore":
                    var settings = new PostgresStreamStoreSettings(connectionString);
                    config.UseSqlStreamStore(new PostgresStreamStore(settings));
                    break;
            }
        });

        return services;
    }
}