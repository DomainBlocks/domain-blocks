using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStoreConfig<TRawData>
{
    public EntityStoreConfig(
        IEventDataSerializer<TRawData> eventDataSerializer,
        IEnumerable<EntityStreamConfig<TRawData>>? streamConfigs = null)
    {
        EventDataSerializer = eventDataSerializer;
        StreamConfigs = (streamConfigs ?? Enumerable.Empty<EntityStreamConfig<TRawData>>())
            .ToDictionary(x => x.EntityType);
    }

    public IEventDataSerializer<TRawData> EventDataSerializer { get; }
    public IReadOnlyDictionary<Type, EntityStreamConfig<TRawData>> StreamConfigs { get; }
}

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