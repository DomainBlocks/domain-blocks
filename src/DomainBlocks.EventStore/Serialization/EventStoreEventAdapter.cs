using DomainBlocks.Core.Serialization;
using EventStore.Client;

namespace DomainBlocks.EventStore.Serialization;

public class EventStoreEventAdapter : IEventAdapter<ResolvedEvent, EventData>
{
    private readonly IEventDataSerializer<ReadOnlyMemory<byte>> _serializer;

    public EventStoreEventAdapter(IEventDataSerializer<ReadOnlyMemory<byte>> serializer)
    {
        _serializer = serializer;
    }

    public string GetEventName(ResolvedEvent @event) => @event.Event.EventType;

    public Task<object> DeserializeEvent(
        ResolvedEvent @event,
        Type eventType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_serializer.Deserialize(@event.Event.Data, eventType));
    }

    public IReadOnlyDictionary<string, string> DeserializeMetadata(ResolvedEvent @event)
    {
        var payload = @event.Event.Metadata;

        return payload.IsEmpty
            ? new Dictionary<string, string>()
            : _serializer.Deserialize<Dictionary<string, string>>(payload);
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