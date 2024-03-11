using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public class EntityStoreOptions<TRawData>
{
    public EntityStoreOptions(
        IEventDataSerializer<TRawData> eventDataSerializer,
        IEnumerable<EntityStreamOptions<TRawData>>? streamOptions = null)
    {
        EventDataSerializer = eventDataSerializer;
        StreamOptions = (streamOptions ?? Enumerable.Empty<EntityStreamOptions<TRawData>>())
            .ToDictionary(x => x.EntityType);
    }

    public IEventDataSerializer<TRawData> EventDataSerializer { get; }
    public IReadOnlyDictionary<Type, EntityStreamOptions<TRawData>> StreamOptions { get; }
}

public class EntityStoreOptions
{
    public EntityStoreOptions(
        EventTypeMap eventTypeMap,
        int? snapshotEventCount = null,
        IEnumerable<EntityStreamOptions>? streamOptions = null)
    {
        EventTypeMap = eventTypeMap;
        SnapshotEventCount = snapshotEventCount;
        StreamOptions = (streamOptions ?? Enumerable.Empty<EntityStreamOptions>()).ToDictionary(x => x.EntityType);
    }

    public EventTypeMap EventTypeMap { get; }
    public int? SnapshotEventCount { get; }
    public IReadOnlyDictionary<Type, EntityStreamOptions> StreamOptions { get; }
}