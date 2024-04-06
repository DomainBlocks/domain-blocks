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

    public IEnumerable<Type> GetMappedEventTypes(StoredEventEntry storedEventEntry)
    {
        // TODO: Remove duplication
        if (!_mappingsByName.TryGetValue(storedEventEntry.Name, out var mapping))
        {
            throw new ArgumentException(
                $"Mapping not found for event name '{storedEventEntry.Name}'.", nameof(storedEventEntry));
        }

        yield return mapping.EventType;
    }

    public IEnumerable<object> ToEventObjects(StoredEventEntry storedEventEntry)
    {
        if (!_mappingsByName.TryGetValue(storedEventEntry.Name, out var mapping))
        {
            throw new ArgumentException(
                $"Mapping not found for event name '{storedEventEntry.Name}'.", nameof(storedEventEntry));
        }

        // Consider ignored event names. Can yield zero events in the case a name is ignored.
        // Will be addressed in a future PR.

        var @event = _serializer.Deserialize(storedEventEntry.Payload.Span, mapping.EventType);

        yield return @event;
    }

    public WritableEventEntry ToWritableEventEntry(object @event)
    {
        if (!_mappingsByType.TryGetValue(@event.GetType(), out var mapping))
        {
            throw new ArgumentException($"Mapping not found for event type '{@event.GetType()}'.", nameof(@event));
        }

        var payload = _serializer.Serialize(@event);

        return new WritableEventEntry(mapping.EventName, payload, default);
    }
}