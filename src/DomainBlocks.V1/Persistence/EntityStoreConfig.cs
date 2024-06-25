using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Persistence.Entities;

namespace DomainBlocks.V1.Persistence;

public class EntityStoreConfig
{
    public EntityStoreConfig(
        IEventStore eventStore,
        EntityAdapterRegistry entityAdapterRegistry,
        EventMapper eventMapper,
        IReadOnlyDictionary<Type, EntityStreamConfig>? streamConfigs = null)
    {
        EventStore = eventStore;
        EntityAdapterRegistry = entityAdapterRegistry;
        EventMapper = eventMapper;
        StreamConfigs = streamConfigs ?? new Dictionary<Type, EntityStreamConfig>();
    }

    public IEventStore EventStore { get; }
    public EntityAdapterRegistry EntityAdapterRegistry { get; }
    public EventMapper EventMapper { get; }
    public IReadOnlyDictionary<Type, EntityStreamConfig> StreamConfigs { get; }
}