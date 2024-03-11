using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

internal delegate IEntityStore EntityStoreFactory(
    EntityAdapterProvider entityAdapterProvider, EntityStoreOptions options);