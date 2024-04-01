using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.V1.Persistence.Builders;

namespace DomainBlocks.V1.SqlStreamStore.Extensions;

public static class EntityStoreBuilderExtensions
{
    public static EntityStoreConfigBuilder UseSqlStreamStore(
        this EntityStoreConfigBuilder builder, IStreamStore streamStore)
    {
        var eventStore = new SqlStreamStoreEventStore(streamStore);
        return builder.SetEventStore(eventStore);
    }
}