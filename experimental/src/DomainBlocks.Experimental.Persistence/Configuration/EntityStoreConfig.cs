using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStoreConfig
{
    public EntityStoreConfig(
        EventTypeMap eventTypeMap,
        int? snapshotEventCount = null,
        IEnumerable<EntityStreamConfig>? streamConfigs = null)
    {
        EventTypeMap = eventTypeMap;
        SnapshotEventCount = snapshotEventCount;
        StreamConfigs = (streamConfigs ?? Enumerable.Empty<EntityStreamConfig>()).ToDictionary(x => x.EntityType);
    }

    public EventTypeMap EventTypeMap { get; }
    public int? SnapshotEventCount { get; }
    public IReadOnlyDictionary<Type, EntityStreamConfig> StreamConfigs { get; }
}

public class EntityStoreConfig<TSerializedData>
{
    public EntityStoreConfig(
        IEventDataSerializer<TSerializedData> eventDataSerializer,
        IEnumerable<EntityStreamConfig<TSerializedData>>? streamConfigs = null)
    {
        EventDataSerializer = eventDataSerializer;
        StreamConfigs = (streamConfigs ?? Enumerable.Empty<EntityStreamConfig<TSerializedData>>())
            .ToDictionary(x => x.EntityType);
    }

    public IEventDataSerializer<TSerializedData> EventDataSerializer { get; }
    public IReadOnlyDictionary<Type, EntityStreamConfig<TSerializedData>> StreamConfigs { get; }
}