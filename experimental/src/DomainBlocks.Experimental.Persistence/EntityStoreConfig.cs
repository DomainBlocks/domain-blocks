using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence;

public class EntityStoreConfig
{
    public EntityStoreConfig(
        IEventStore eventStore,
        EntityAdapterProvider entityAdapterProvider,
        EventMapper eventMapper,
        IEnumerable<EntityStreamConfig>? streamConfigs = null)
    {
        EventStore = eventStore;
        EntityAdapterProvider = entityAdapterProvider;
        EventMapper = eventMapper;
        StreamConfigs = (streamConfigs ?? Enumerable.Empty<EntityStreamConfig>()).ToDictionary(x => x.EntityType);
    }

    public IEventStore EventStore { get; }
    public EntityAdapterProvider EntityAdapterProvider { get; }
    public EventMapper EventMapper { get; }
    public IReadOnlyDictionary<Type, EntityStreamConfig> StreamConfigs { get; }
}