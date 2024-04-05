using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1;

public class EventMapper
{
    private readonly IReadOnlyDictionary<Type, EventTypeMapping> _mappingsByType;
    private readonly IReadOnlyDictionary<string, EventTypeMapping> _mappingsByName;
    private readonly ISerializer _serializer;

    public EventMapper(IEnumerable<EventTypeMapping> mappings, ISerializer serializer)
    {
        _mappingsByType = mappings.ToDictionary(x => x.EventType);

        _mappingsByName = _mappingsByType.Values
            .SelectMany(x => Enumerable.Repeat(x.EventName, 1).Concat(x.DeprecatedEventNames),
                (mapping, eventName) => (eventName, mapping))
            .ToDictionary(x => x.eventName, x => x.mapping);

        _serializer = serializer;
    }

    public IEnumerable<object> FromReadEvent(ReadEventRecord readEventRecord)
    {
        if (!_mappingsByName.TryGetValue(readEventRecord.Name, out var mapping))
        {
            throw new ArgumentException($"Mapping not found for event name '{readEventRecord.Name}'.", nameof(readEventRecord));
        }

        // Consider ignored event names. Can yield zero events in the case a name is ignored.
        // Will be addressed in a future PR.

        var @event = _serializer.Deserialize(readEventRecord.Payload.Span, mapping.EventType);

        yield return @event;
    }

    public WriteEventRecord ToWriteEvent(object @event)
    {
        if (!_mappingsByType.TryGetValue(@event.GetType(), out var mapping))
        {
            throw new ArgumentException($"Mapping not found for event type '{@event.GetType()}'.", nameof(@event));
        }

        var payload = _serializer.Serialize(@event);

        return new WriteEventRecord(mapping.EventName, payload, default);
    }
}