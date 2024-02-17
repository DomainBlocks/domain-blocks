namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public class ExtendedEventTypeMap<TState>
{
    private readonly IReadOnlyDictionary<Type, ExtendedEventTypeMapping<TState>> _mappingsByType;
    private readonly ILookup<string, ExtendedEventTypeMapping<TState>> _mappingsByName;

    public ExtendedEventTypeMap(IEnumerable<ExtendedEventTypeMapping<TState>> mappings)
    {
        _mappingsByType = mappings.ToDictionary(x => x.EventType);

        _mappingsByName = _mappingsByType.Values
            .SelectMany(x => x.GetAllEventNames(), (mapping, eventName) => (eventName, mapping))
            .ToLookup(x => x.eventName, x => x.mapping);
    }

    public ExtendedEventTypeMapping<TState> this[Type eventType]
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

    public string GetEventName(Type eventType)
    {
        if (_mappingsByType.TryGetValue(eventType, out var mapping))
        {
            return mapping.EventName;
        }

        throw new ArgumentException($"Mapping not found for event type '{eventType}'.", nameof(eventType));
    }

    public bool IsEventNameIgnored(string eventName)
    {
        // TODO
        return false;
    }

    public Type ResolveEventType(string eventName, IReadOnlyDictionary<string, string> eventMetadata)
    {
        // TODO: Come up with a better model for this
        var mappings = _mappingsByName[eventName].ToList();
        var defaultMapping = mappings.Single(x => x.ReadCondition == null);

        var conditionalMapping = mappings
            .Where(x => x.ReadCondition != null)
            .FirstOrDefault(x => x.ReadCondition!(eventMetadata));

        return conditionalMapping == null ? defaultMapping.EventType : conditionalMapping.EventType;
    }
}