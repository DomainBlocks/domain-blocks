using DomainBlocks.Experimental.Persistence.Adapters;

namespace DomainBlocks.Experimental.Persistence.Configuration;

internal delegate IEntityStore EntityStoreFactory(
    EntityAdapterProvider entityAdapterProvider, EntityStoreConfig config);