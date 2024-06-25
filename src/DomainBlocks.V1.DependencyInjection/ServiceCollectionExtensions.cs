using DomainBlocks.V1.Persistence;
using DomainBlocks.V1.Persistence.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.V1.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntityStore(
        this IServiceCollection services, Action<EntityStoreConfigBuilder> builderAction)
    {
        return services.AddEntityStore((_, config) => builderAction(config));
    }

    public static IServiceCollection AddEntityStore(
        this IServiceCollection services, Action<IServiceProvider, EntityStoreConfigBuilder> builderAction)
    {
        return services.AddSingleton<IEntityStore>(sp =>
        {
            var builder = new EntityStoreConfigBuilder();
            builderAction(sp, builder);
            var config = builder.Build();
            return new EntityStore(config);
        });
    }
}