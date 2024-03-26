using DomainBlocks.Experimental.Persistence.Builders;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.Experimental.Persistence.SqlStreamStore;

public static class SqlStreamStoreEntityStoreBuilderExtensions
{
    // TODO: support builder to specify postgres, etc.
    public static EntityStoreConfigBuilder UseSqlStreamStore(
        this EntityStoreConfigBuilder builder, IStreamStore streamStore)
    {
        var eventStore = new SqlStreamStoreEventStore(streamStore);
        return builder.SetEventStore(eventStore);
    }
}