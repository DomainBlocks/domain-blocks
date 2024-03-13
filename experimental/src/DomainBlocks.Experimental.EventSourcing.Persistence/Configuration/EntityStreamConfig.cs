using DomainBlocks.Experimental.EventSourcing.Persistence.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public class EntityStreamConfig<TRawData>
{
    public EntityStreamConfig(Type entityType, IEventDataSerializer<TRawData> eventDataSerializer)
    {
        EventDataSerializer = eventDataSerializer;
        EntityType = entityType;
    }

    public Type EntityType { get; }
    public IEventDataSerializer<TRawData> EventDataSerializer { get; }
}

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