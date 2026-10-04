namespace DomainBlocks.EventStore.TypeMapping;

public class EventTypeMapBuilder
{
    private readonly List<EventTypeMapping> _mappings = [];

    public EventTypeMapBuilder Add<TEvent>(string? eventName = null) => Add(typeof(TEvent), eventName);

    public EventTypeMapBuilder Add(Type eventType, string? eventName = null)
    {
        var resolvedName = ResolveName(eventType, eventName);
        _mappings.Add(new EventTypeMapping.Write(eventType, resolvedName));
        _mappings.Add(new EventTypeMapping.ReadToType(resolvedName, eventType));
        return this;
    }

    public EventTypeMapBuilder AddWrite<TEvent>(string? eventName = null) => AddWrite(typeof(TEvent), eventName);

    public EventTypeMapBuilder AddWrite(Type eventType, string? eventName = null)
    {
        var resolvedName = ResolveName(eventType, eventName);
        _mappings.Add(new EventTypeMapping.Write(eventType, resolvedName));
        return this;
    }

    public EventTypeMapBuilder AddRead<TEvent>(params string[] eventNames) => AddRead(typeof(TEvent), eventNames);

    public EventTypeMapBuilder AddRead(Type eventType, params string[] eventNames)
    {
        if (eventNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Event names cannot be null or whitespace.", nameof(eventNames));

        if (eventNames.Length == 0)
        {
            _mappings.Add(new EventTypeMapping.ReadToType(eventType.Name, eventType));
        }
        else
        {
            foreach (var eventName in eventNames)
                _mappings.Add(new EventTypeMapping.ReadToType(eventName, eventType));
        }

        return this;
    }

    public EventTypeMapBuilder AddRead(object instance, params string[] eventNames)
    {
        if (eventNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Event names cannot be null or whitespace.", nameof(eventNames));

        if (eventNames.Length == 0)
        {
            throw new ArgumentException(
                "At least one event name is required when mapping to an instance.",
                nameof(eventNames));
        }

        foreach (var eventName in eventNames)
            _mappings.Add(new EventTypeMapping.ReadToInstance(eventName, instance));

        return this;
    }

    public EventTypeMap Build()
    {
        return _mappings.Count > 0
            ? EventTypeMap.Create(_mappings)
            : throw new InvalidOperationException("No event types are mapped.");
    }

    private static string ResolveName(Type eventType, string? eventName)
    {
        if (eventName is not null && string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be empty or whitespace.", nameof(eventName));

        return eventName ?? eventType.Name;
    }
}