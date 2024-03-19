using DomainBlocks.Experimental.Persistence.Entities;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public delegate IEntityStore EntityStoreFactory(EntityAdapterProvider entityAdapterProvider, EntityStoreConfig config);