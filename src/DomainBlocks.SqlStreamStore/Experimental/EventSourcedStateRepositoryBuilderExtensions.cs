using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.SqlStreamStore.Experimental;

public static class EventSourcedStateRepositoryBuilderExtensions
{
    // TODO: support builder to specify postgres, etc.
    public static EventSourcedStateRepositoryBuilder<string> UseSqlStreamStore(
        this EventSourcedStateRepositoryBuilder @this,
        IStreamStore streamStore)
    {
        return @this.Use(new SqlStreamStoreEventRepository(streamStore), new SqlStreamStoreEventAdapter());
    }
}