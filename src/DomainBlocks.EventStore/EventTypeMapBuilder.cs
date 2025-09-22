namespace DomainBlocks.EventStore;

/// <summary>
/// Builds an <see cref="EventTypeMap"/> by registering mappings between event CLR types and string names.
/// </summary>
public sealed class EventTypeMapBuilder
{
    private readonly HashSet<EventTypeToNameMapping> _writeMappings = [];
    private readonly HashSet<EventNameToTypeMapping> _readMappings = [];

    /// <summary>
    /// Maps an event CLR type with a name for both writing and reading.
    /// </summary>
    /// <typeparam name="TEvent">The event type to map.</typeparam>
    /// <param name="eventName">The event name to map. If omitted, <c>typeof(TEvent).Name</c> is used.</param>
    /// <returns>The current <see cref="EventTypeMapBuilder"/> instance.</returns>
    public EventTypeMapBuilder MapType<TEvent>(string? eventName = null)
    {
        var eventType = typeof(TEvent);
        eventName ??= eventType.Name;

        _writeMappings.Add(new EventTypeToNameMapping(typeof(TEvent), eventName));
        _readMappings.Add(new EventNameToTypeMapping(eventName, typeof(TEvent)));

        return this;
    }

    /// <summary>
    /// Maps one or more event names to a single CLR type for reading.
    /// </summary>
    /// <typeparam name="TEvent">The event type to map to.</typeparam>
    /// <param name="eventNames">One or more event names to map from.</param>
    /// <returns>The current <see cref="EventTypeMapBuilder"/> instance.</returns>
    public EventTypeMapBuilder MapReadType<TEvent>(params string[] eventNames)
    {
        var mappings = eventNames.Select(x => new EventNameToTypeMapping(x, typeof(TEvent)));

        foreach (var mapping in mappings)
            _readMappings.Add(mapping);

        return this;
    }

    /// <summary>
    /// Builds an <see cref="EventTypeMap"/> from this builder.
    /// </summary>
    public EventTypeMap Build() => new(_writeMappings, _readMappings);
}