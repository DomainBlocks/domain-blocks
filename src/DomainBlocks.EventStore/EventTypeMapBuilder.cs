namespace DomainBlocks.EventStore;

/// <summary>
/// Builds an <see cref="EventTypeMap"/> by registering mappings between event CLR types and string names.
/// </summary>
public sealed class EventTypeMapBuilder
{
    private readonly EventTypeToNameMappingSet _writeMappings = new();
    private readonly EventNameToTypeMappingSet _readMappings = new();

    /// <summary>
    /// Maps an event CLR type to a name for both writing (type-to-name) and reading (name-to-type).
    /// </summary>
    /// <typeparam name="TEvent">The event type to map.</typeparam>
    /// <param name="eventName">The event name to map. If omitted, <c>typeof(TEvent).Name</c> is used.</param>
    /// <returns>The current <see cref="EventTypeMapBuilder"/> instance.</returns>
    /// <exception cref="DomainBlocks.EventStore.Exceptions.EventTypeMapConfigurationException">
    /// Thrown when applying the mapping would violate any of the following constraints:
    /// <list type="bullet">
    ///   <item>One-to-one for type-to-name (write-side) mappings.</item>
    ///   <item>Many-to-one for name-to-type (read-side) mappings.</item>
    /// </list>
    /// </exception>
    public EventTypeMapBuilder MapType<TEvent>(string? eventName = null)
    {
        var eventType = typeof(TEvent);
        eventName ??= eventType.Name;

        _writeMappings.Add<TEvent>(eventName);
        _readMappings.Add<TEvent>(eventName);

        return this;
    }

    /// <summary>
    /// Maps one or more event names to a CLR type for reading (name-to-type).
    /// </summary>
    /// <typeparam name="TEvent">The event type to map to.</typeparam>
    /// <param name="eventNames">One or more event names to map from.</param>
    /// <returns>The current <see cref="EventTypeMapBuilder"/> instance.</returns>
    /// <exception cref="DomainBlocks.EventStore.Exceptions.EventTypeMapConfigurationException">
    /// Thrown when applying the mapping would violate the read-side many-to-one constraint for name-to-type mappings.
    /// </exception>
    public EventTypeMapBuilder MapReadType<TEvent>(params string[] eventNames)
    {
        _readMappings.Add<TEvent>(eventNames);
        return this;
    }

    /// <summary>
    /// Builds an <see cref="EventTypeMap"/> from this builder.
    /// </summary>
    public EventTypeMap Build() => new(_writeMappings, _readMappings);
}