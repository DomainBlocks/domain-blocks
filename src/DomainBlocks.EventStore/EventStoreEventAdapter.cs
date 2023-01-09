using EventStore.Client;

// ReSharper disable once CheckNamespace
namespace DomainBlocks.Core.Serialization.EventStore;

public class EventStoreEventAdapter : IEventAdapter<EventRecord, EventData>
{
    private readonly IEventDataSerializer<ReadOnlyMemory<byte>> _serializer;

    public EventStoreEventAdapter(IEventDataSerializer<ReadOnlyMemory<byte>> serializer)
    {
        _serializer = serializer;
    }

    public string GetEventName(EventRecord readEvent) => readEvent.EventType;

    public ValueTask<object> DeserializeEvent(
        EventRecord readEvent,
        Type eventType,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_serializer.Deserialize(readEvent.Data, eventType));
    }

    public IReadOnlyDictionary<string, string> DeserializeMetadata(EventRecord readEvent)
    {
        return (IReadOnlyDictionary<string, string>)_serializer.Deserialize(
            readEvent.Metadata,
            typeof(IReadOnlyDictionary<string, string>));
    }

    public EventData SerializeToWriteEvent(
        object @event,
        string eventName,
        IEnumerable<KeyValuePair<string, string>> metadata)
    {
        var serializedEvent = _serializer.Serialize(@event);
        var serializedMetadata = _serializer.Serialize(metadata);
        return new EventData(Uuid.NewUuid(), eventName, serializedEvent, serializedMetadata, _serializer.ContentType);
    }
}