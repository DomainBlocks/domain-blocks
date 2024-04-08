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

    public IEnumerable<Type> GetMappedEventTypes(StoredEventRecord storedEventRecord)
    {
        // TODO: Remove duplication
        if (!_mappingsByName.TryGetValue(storedEventRecord.Name, out var mapping))
        {
            throw new ArgumentException(
                $"Mapping not found for event name '{storedEventRecord.Name}'.", nameof(storedEventRecord));
        }

        yield return mapping.EventType;
    }

    public IEnumerable<object> ToEventObjects(StoredEventRecord storedEventRecord)
    {
        if (!_mappingsByName.TryGetValue(storedEventRecord.Name, out var mapping))
        {
            throw new ArgumentException(
                $"Mapping not found for event name '{storedEventRecord.Name}'.", nameof(storedEventRecord));
        }

        // Consider ignored event names. Can yield zero events in the case a name is ignored.
        // Will be addressed in a future PR.

        var @event = _serializer.Deserialize(storedEventRecord.Payload.Span, mapping.EventType);

        yield return @event;
    }

    public WritableEventRecord ToWritableEventRecord(object @event)
    {
        if (!_mappingsByType.TryGetValue(@event.GetType(), out var mapping))
        {
            throw new ArgumentException($"Mapping not found for event type '{@event.GetType()}'.", nameof(@event));
        }

        var payload = _serializer.Serialize(@event);

        return new WritableEventRecord(mapping.EventName, payload, default);
    }
}