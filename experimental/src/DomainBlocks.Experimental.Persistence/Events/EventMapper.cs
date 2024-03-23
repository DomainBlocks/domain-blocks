using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Events;

public class EventMapper : IEventMapper
{
    private readonly IReadOnlyDictionary<Type, EventTypeMapping> _mappingsByType;
    private readonly IReadOnlyDictionary<string, EventTypeMapping> _mappingsByName;

    public EventMapper(IEnumerable<EventTypeMapping> mappings, IEventDataSerializer serializer)
    {
        _mappingsByType = mappings.ToDictionary(x => x.EventType);

        _mappingsByName = _mappingsByType.Values
            .SelectMany(x => Enumerable.Repeat(x.EventName, 1).Concat(x.DeprecatedEventNames),
                (mapping, eventName) => (eventName, mapping))
            .ToDictionary(x => x.eventName, x => x.mapping);

        Serializer = serializer;
    }

    public IEventDataSerializer Serializer { get; }

    public bool IsEventNameIgnored(string eventName)
    {
        // TODO
        return false;
    }

    public IEnumerable<object> FromReadEvent(ReadEvent readEvent)
    {
        if (!_mappingsByName.TryGetValue(readEvent.Name, out var mapping))
        {
            throw new ArgumentException($"Mapping not found for event name '{readEvent.Name}'.", nameof(readEvent));
        }

        var @event = Serializer.Deserialize(readEvent.Payload.Span, mapping.EventType);

        yield return @event;
    }

    public WriteEvent ToWriteEvent(object @event)
    {
        if (!_mappingsByType.TryGetValue(@event.GetType(), out var mapping))
        {
            throw new ArgumentException($"Mapping not found for event type '{@event.GetType()}'.", nameof(@event));
        }

        var payload = Serializer.Serialize(@event);

        return new WriteEvent(mapping.EventName, payload, default);
    }
}