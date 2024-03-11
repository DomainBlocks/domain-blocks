using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public class EntityStreamOptions<TRawData>
{
    public EntityStreamOptions(Type entityType, IEventDataSerializer<TRawData> eventDataSerializer)
    {
        EventDataSerializer = eventDataSerializer;
        EntityType = entityType;
    }

    public Type EntityType { get; }
    public IEventDataSerializer<TRawData> EventDataSerializer { get; }
}

public class EntityStreamOptions
{
    public EntityStreamOptions(
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