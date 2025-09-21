namespace DomainBlocks.EventStore;

public sealed class EventTypeMapBuilder
{
    private readonly HashSet<EventTypeToNameMapping> _writeMappings = [];
    private readonly HashSet<EventNameToTypeMapping> _readMappings = [];

    public EventTypeMapBuilder MapType<TEvent>(string? eventName = null)
    {
        var eventType = typeof(TEvent);
        eventName ??= eventType.Name;

        _writeMappings.Add(new EventTypeToNameMapping(typeof(TEvent), eventName));
        _readMappings.Add(new EventNameToTypeMapping(eventName, typeof(TEvent)));

        return this;
    }

    public EventTypeMapBuilder MapReadType<TEvent>(params string[] eventNames)
    {
        var mappings = eventNames.Select(x => new EventNameToTypeMapping(x, typeof(TEvent)));

        foreach (var mapping in mappings)
            _readMappings.Add(mapping);

        return this;
    }

    public EventTypeMap Build() => new(_writeMappings, _readMappings);
}