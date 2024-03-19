using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.Experimental.Persistence.SqlStreamStore;

public static class SqlStreamStoreEntityStoreBuilderExtensions
{
    // TODO: support builder to specify postgres, etc.
    public static EntityStoreBuilder UseSqlStreamStore(
        this EntityStoreBuilder builder,
        IStreamStore streamStore,
        Action<EntityStoreConfigBuilder<string>>? builderAction = null)
    {
        var dataConfigBuilder = new EntityStoreConfigBuilder<string>();
        builderAction?.Invoke(dataConfigBuilder);
        var dataConfig = dataConfigBuilder.Build();

        var eventStore = new SqlStreamStoreEventStore(streamStore);
        return builder.SetInfrastructure(eventStore, dataConfig);
    }
}