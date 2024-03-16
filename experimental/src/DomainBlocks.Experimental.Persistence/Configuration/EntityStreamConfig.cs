using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStreamConfig
{
    public EntityStreamConfig(
        Type entityType,
        EventTypeMap eventTypeMap,
        IEventDataSerializer? eventDataSerializer,
        int? snapshotEventCount = null,
        string? streamIdPrefix = null)
    {
        EntityType = entityType;
        EventTypeMap = eventTypeMap;
        EventDataSerializer = eventDataSerializer;
        SnapshotEventCount = snapshotEventCount;
        StreamIdPrefix = streamIdPrefix;
    }

    public Type EntityType { get; }
    public EventTypeMap EventTypeMap { get; }
    public IEventDataSerializer? EventDataSerializer { get; }
    public int? SnapshotEventCount { get; }
    public string? StreamIdPrefix { get; }
}