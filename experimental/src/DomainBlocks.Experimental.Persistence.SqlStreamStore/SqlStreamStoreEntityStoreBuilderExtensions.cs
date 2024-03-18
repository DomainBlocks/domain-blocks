using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.Experimental.Persistence.Events;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.Experimental.Persistence.SqlStreamStore;

public static class SqlStreamStoreEntityStoreBuilderExtensions
{
    // TODO: support builder to specify postgres, etc.
    public static EntityStoreBuilder UseSqlStreamStore(this EntityStoreBuilder builder, IStreamStore streamStore)
    {
        var eventStore = new SqlStreamStoreEventStore(streamStore);
        var writeEventFactory = new StringWriteEventFactory();
        return builder.SetInfrastructure(eventStore, writeEventFactory);
    }
}