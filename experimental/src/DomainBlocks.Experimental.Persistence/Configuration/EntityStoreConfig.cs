using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStoreConfig
{
    public EntityStoreConfig(
        EventTypeMap eventTypeMap,
        IEventDataSerializer eventDataSerializer,
        int? snapshotEventCount = null,
        IEnumerable<EntityStreamConfig>? streamConfigs = null)
    {
        EventTypeMap = eventTypeMap;
        EventDataSerializer = eventDataSerializer;
        SnapshotEventCount = snapshotEventCount;
        StreamConfigs = (streamConfigs ?? Enumerable.Empty<EntityStreamConfig>()).ToDictionary(x => x.EntityType);
    }

    public EventTypeMap EventTypeMap { get; }
    public IEventDataSerializer EventDataSerializer { get; }
    public int? SnapshotEventCount { get; }
    public IReadOnlyDictionary<Type, EntityStreamConfig> StreamConfigs { get; }
}