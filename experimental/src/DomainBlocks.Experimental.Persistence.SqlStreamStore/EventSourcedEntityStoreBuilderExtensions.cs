﻿using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.Experimental.Persistence.SqlStreamStore;

public static class EventSourcedEntityStoreBuilderExtensions
{
    // TODO: support builder to specify postgres, etc.
    public static EntityStoreBuilder UseSqlStreamStore(this EntityStoreBuilder builder, IStreamStore streamStore)
    {
        var eventStore = new SqlStreamStoreEventStore(streamStore);
        var eventAdapter = new SqlStreamStoreEventAdapter();

        return builder.SetInfrastructure(eventStore, eventAdapter);
    }
}