using System.Collections.Frozen;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.Events;

public class EventSerializer<TPayload>
{
    private readonly FrozenDictionary<Type, EventTypeMapping> _mappingsByType;
    private readonly FrozenDictionary<string, EventTypeMapping> _mappingsByName;
    private readonly ISerializer<TPayload> _serializer;

    public EventSerializer(IEnumerable<EventTypeMapping> mappings, ISerializer<TPayload> serializer)
    {
        _mappingsByType = mappings.ToFrozenDictionary(x => x.EventType);
        _mappingsByName = _mappingsByType.Values.ToFrozenDictionary(x => x.EventName);
        _serializer = serializer;
    }

    public (string EventName, TPayload Payload) Serialize(object value)
    {
        var eventType = value.GetType();

        if (!_mappingsByType.TryGetValue(eventType, out var mapping))
        {
            throw new EventTypeMappingNotFoundException(eventType);
        }

        var payload = _serializer.Serialize(value);

        return (mapping.EventName, payload);
    }

    public object Deserialize(string eventName, TPayload payload)
    {
        if (!_mappingsByName.TryGetValue(eventName, out var mapping))
        {
            throw new EventTypeMappingNotFoundException(eventName);
        }

        return _serializer.Deserialize(payload, mapping.EventType);
    }
}