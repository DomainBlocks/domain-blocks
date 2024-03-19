using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStreamConfig
{
    public EntityStreamConfig(
        Type entityType,
        EventTypeMap eventTypeMap,
        int? snapshotEventCount = null,
        string? streamIdPrefix = null)
    {
        EntityType = entityType;
        EventTypeMap = eventTypeMap;
        SnapshotEventCount = snapshotEventCount;
        StreamIdPrefix = streamIdPrefix;
    }

    public Type EntityType { get; }
    public EventTypeMap EventTypeMap { get; }
    public int? SnapshotEventCount { get; }
    public string? StreamIdPrefix { get; }
}

public class EntityStreamConfig<TSerializedData>
{
    public EntityStreamConfig(Type entityType, IEventDataSerializer<TSerializedData>? eventDataSerializer)
    {
        EntityType = entityType;
        EventDataSerializer = eventDataSerializer;
    }

    public Type EntityType { get; }
    public IEventDataSerializer<TSerializedData>? EventDataSerializer { get; }
}