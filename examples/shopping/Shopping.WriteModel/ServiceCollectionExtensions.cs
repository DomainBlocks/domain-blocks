using DomainBlocks.Persistence.DependencyInjection;
using DomainBlocks.Persistence.EventStoreDb.Extensions;
using DomainBlocks.Persistence.SqlStreamStore.Extensions;
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
        if (eventStore == null)
        {
            throw new ArgumentNullException(nameof(eventStore),"Could not find EventStore configuration value.");
        }
        
        var connectionString = configuration.GetConnectionString(eventStore);

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString),
                "Could not find connection string for EventStore.");
        }
        
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