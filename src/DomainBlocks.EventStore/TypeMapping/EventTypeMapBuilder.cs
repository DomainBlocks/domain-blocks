namespace DomainBlocks.EventStore.TypeMapping;

/// <summary>
/// Builds an <see cref="EventTypeMap"/> by registering mappings between event CLR types and string names.
/// </summary>
public class EventTypeMapBuilder
{
    private readonly AppendEventTypeMappingSet _appendMappings = new();
    private readonly ReadEventTypeMappingSet _readMappings = new();

    public EventTypeMapBuilder()
    {
        Append = new AppendBuilder(this);
        Read = new ReadBuilder(this);
    }

    public AppendBuilder Append { get; }

    public ReadBuilder Read { get; }

    /// <summary>
    /// Maps an event CLR type to a name for both appending (type-to-name) and reading (name-to-type).
    /// </summary>
    /// <typeparam name="TEvent">The event type to map.</typeparam>
    /// <param name="eventName">The event name to map. If omitted, <c>typeof(TEvent).Name</c> is used.</param>
    /// <returns>The current <see cref="EventTypeMapBuilder"/> instance.</returns>
    /// <exception cref="EventTypeMapConfigurationException">
    /// Thrown when applying the mapping would violate any of the following constraints:
    /// <list type="bullet">
    ///   <item>One-to-one for type-to-name (write-side) mappings.</item>
    ///   <item>Many-to-one for name-to-type (read-side) mappings.</item>
    /// </list>
    /// </exception>
    public EventTypeMapBuilder MapType<TEvent>(string? eventName = null)
    {
        eventName ??= typeof(TEvent).Name;
        _appendMappings.Add<TEvent>(eventName);
        _readMappings.Add<TEvent>(eventName);
        return this;
    }

    /// <summary>
    /// Builds an <see cref="EventTypeMap"/> from this builder.
    /// </summary>
    public EventTypeMap Build() => new(_appendMappings, _readMappings);

    public class AppendBuilder
    {
        private readonly EventTypeMapBuilder _rootBuilder;

        internal AppendBuilder(EventTypeMapBuilder rootBuilder)
        {
            _rootBuilder = rootBuilder;
        }

        public EventTypeMapBuilder MapType<TEvent>(string? eventName = null)
        {
            eventName ??= typeof(TEvent).Name;
            _rootBuilder._appendMappings.Add<TEvent>(eventName);
            return _rootBuilder;
        }
    }

    public class ReadBuilder
    {
        private readonly EventTypeMapBuilder _rootBuilder;

        internal ReadBuilder(EventTypeMapBuilder rootBuilder)
        {
            _rootBuilder = rootBuilder;
        }

        /// <summary>
        /// Maps one or more event names to a CLR type for reading (name-to-type).
        /// </summary>
        /// <typeparam name="TEvent">The event type to map to.</typeparam>
        /// <param name="eventNames">One or more event names to map from.</param>
        /// <returns>The current <see cref="EventTypeMapBuilder"/> instance.</returns>
        /// <exception cref="EventTypeMapConfigurationException">
        /// Thrown when applying the mapping would violate the read-side many-to-one constraint for name-to-type mappings.
        /// </exception>
        public EventTypeMapBuilder MapType<TEvent>(params string[] eventNames)
        {
            _rootBuilder._readMappings.Add<TEvent>(eventNames);
            return _rootBuilder;
        }
    }
}