using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence;

public class EntityStoreConfig
{
    public EntityStoreConfig(
        IEventStore eventStore,
        EntityAdapterProvider entityAdapterProvider,
        EventMapper eventMapper,
        int? snapshotEventCount = null,
        IEnumerable<EntityStreamConfig>? streamConfigs = null)
    {
        EventStore = eventStore;
        EntityAdapterProvider = entityAdapterProvider;
        EventMapper = eventMapper;
        SnapshotEventCount = snapshotEventCount;
        StreamConfigs = (streamConfigs ?? Enumerable.Empty<EntityStreamConfig>()).ToDictionary(x => x.EntityType);
    }

    public IEventStore EventStore { get; }
    public EntityAdapterProvider EntityAdapterProvider { get; }
    public EventMapper EventMapper { get; }
    public int? SnapshotEventCount { get; }
    public IReadOnlyDictionary<Type, EntityStreamConfig> StreamConfigs { get; }
}