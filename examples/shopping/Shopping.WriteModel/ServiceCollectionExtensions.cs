using DomainBlocks.Persistence.DependencyInjection;
using DomainBlocks.Persistence.EventStoreDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Domain.Events;

namespace Shopping.WriteModel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShoppingWriteModel(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetValue<string>("EventStore:ConnectionString")!;

        services.AddEntityStore(config => config
            .UseEventStoreDb(connectionString)
            .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(AggregateAdapter<>)))
            .MapEvents(x => x.MapAll<IDomainEvent>()));

        return services;
    }
}