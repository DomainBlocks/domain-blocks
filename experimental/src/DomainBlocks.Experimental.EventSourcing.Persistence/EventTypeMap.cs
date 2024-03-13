using System.Collections;

namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public class EventTypeMap : IEnumerable<EventTypeMapping>
{
    private readonly IReadOnlyDictionary<Type, EventTypeMapping> _mappingsByType;
    private readonly IReadOnlyDictionary<string, EventTypeMapping> _mappingsByName;

    public EventTypeMap(IEnumerable<EventTypeMapping> mappings)
    {
        _mappingsByType = mappings.ToDictionary(x => x.EventType);

        // TODO: Better error handling for one name to many types
        // One type to many names - OK
        // One name to many types - Not OK
        _mappingsByName = _mappingsByType.Values
            .SelectMany(x => Enumerable.Repeat(x.EventName, 1).Concat(x.DeprecatedEventNames),
                (mapping, eventName) => (eventName, mapping))
            .ToDictionary(x => x.eventName, x => x.mapping);
    }

    public EventTypeMapping this[Type eventType]
    {
        get
        {
            if (!_mappingsByType.TryGetValue(eventType, out var mapping))
            {
                throw new ArgumentException($"Mapping not found for event type '{eventType}'.", nameof(eventType));
            }

            return mapping;
        }
    }

    public EventTypeMapping this[string eventName]
    {
        get
        {
            if (!_mappingsByName.TryGetValue(eventName, out var mapping))
            {
                throw new ArgumentException($"Mapping not found for event name '{eventName}'.", nameof(eventName));
            }

            return mapping;
        }
    }

    public bool IsEventNameIgnored(string eventName)
    {
        // TODO
        return false;
    }

    public IEnumerator<EventTypeMapping> GetEnumerator() => _mappingsByType.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}