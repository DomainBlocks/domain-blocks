using System.Collections.Frozen;

namespace DomainBlocks.EventStore;

public class EventTypeMapper
{
    private readonly FrozenDictionary<Type, EventTypeMapping> _mappingsByType;
    private readonly FrozenDictionary<string, EventTypeMapping> _mappingsByName;

    public EventTypeMapper(IEnumerable<EventTypeMapping> mappings)
    {
        _mappingsByType = mappings.ToFrozenDictionary(x => x.EventType);
        _mappingsByName = _mappingsByType.Values.ToFrozenDictionary(x => x.EventName);
    }
    
    public string GetEventName(object value)
    {
        var eventType = value.GetType();

        return !_mappingsByType.TryGetValue(eventType, out var mapping)
            ? throw new EventTypeMappingNotFoundException(eventType)
            : mapping.EventName;
    }

    public Type GetEventType(string eventName)
    {
        return !_mappingsByName.TryGetValue(eventName, out var mapping)
            ? throw new EventTypeMappingNotFoundException(eventName)
            : mapping.EventType;
    }
}