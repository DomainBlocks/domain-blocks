using DomainBlocks.Persistence.Builders;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore;

public static class SqlStreamStoreEntityStoreBuilderExtensions
{
    public static EntityStoreConfigBuilder UseSqlStreamStore(
        this EntityStoreConfigBuilder builder, IStreamStore streamStore)
    {
        var eventStore = new SqlStreamStoreEventStore(streamStore);
        return builder.SetEventStore(eventStore);
    }
}