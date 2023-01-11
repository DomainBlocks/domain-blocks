using DomainBlocks.Core.Serialization;
using SqlStreamStore.Streams;

namespace DomainBlocks.SqlStreamStore.Serialization;

public sealed class SqlStreamStoreEventAdapter : IEventAdapter<StreamMessage, NewStreamMessage>
{
    private readonly IEventDataSerializer<string> _serializer;

    public SqlStreamStoreEventAdapter(IEventDataSerializer<string> serializer)
    {
        _serializer = serializer;
    }

    public string GetEventName(StreamMessage @event) => @event.Type;

    public async Task<object> DeserializeEvent(
        StreamMessage @event,
        Type eventType,
        CancellationToken cancellationToken = default)
    {
        var eventData = await @event.GetJsonData(cancellationToken);
        return _serializer.Deserialize(eventData, eventType);
    }

    public IReadOnlyDictionary<string, string> DeserializeMetadata(StreamMessage @event)
    {
        var payload = @event.JsonMetadata;

        return payload == null || string.IsNullOrEmpty(payload)
            ? new Dictionary<string, string>()
            : _serializer.Deserialize<Dictionary<string, string>>(payload);
    }

    public NewStreamMessage SerializeToWriteEvent(
        object @event,
        string? eventName,
        IEnumerable<KeyValuePair<string, string>> metadata)
    {
        var serializedEvent = _serializer.Serialize(@event);
        var serializedMetadata = _serializer.Serialize(metadata);
        return new NewStreamMessage(Guid.NewGuid(), eventName, serializedEvent, serializedMetadata);
    }
}