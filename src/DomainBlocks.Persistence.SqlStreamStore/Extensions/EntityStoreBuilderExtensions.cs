using DomainBlocks.Persistence.Builders;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore.Extensions;

public static class EntityStoreBuilderExtensions
{
    public static EntityStoreConfigBuilder UseSqlStreamStore(
        this EntityStoreConfigBuilder builder, IStreamStore streamStore)
    {
        var eventStore = new SqlStreamStoreEventStore(streamStore);
        return builder.SetEventStore(eventStore);
    }
}