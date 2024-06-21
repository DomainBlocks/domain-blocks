using DomainBlocks.V1.DependencyInjection;
using DomainBlocks.V1.EventStoreDb.Extensions;
using DomainBlocks.V1.SqlStreamStore.Extensions;
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
        var eventStoreType = configuration.GetValue<string>("EventStoreType") ?? 
                             throw new InvalidOperationException("Unable to find EventStoreType in configuration.");
        
        var connectionString = configuration.GetConnectionString(eventStoreType) ??
                                 throw new InvalidOperationException($"Unable to find connection string " +
                                                                     $"for {eventStoreType}.");

        services.AddEntityStore(config =>
        {
            config
                .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(AggregateAdapter<>)))
                .MapEvents(x => x.MapAll<IDomainEvent>());

            switch (eventStoreType)
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