namespace DomainBlocks.Experimental.EventSourcing;

internal static class EventTypeMapping
{
    public static EventTypeMapping<TState>.Builder CreateBuilder<TState, TEvent>()
    {
        return CreateBuilder<TState>(typeof(TEvent));
    }

    public static EventTypeMapping<TState>.Builder CreateBuilder<TState>(Type eventType)
    {
        return new EventTypeMapping<TState>(eventType).ToBuilder();
    }
}

/// <summary>
/// Represents a mapping from an event type to an event-to-state applier function, along with other "extension"
/// properties which are used to determine how events of the specified type are processed.
/// </summary>
/// <typeparam name="TState">The type of state to which events of the specified type apply.</typeparam>
public sealed class EventTypeMapping<TState>
{
    internal EventTypeMapping(Type eventType) : this(eventType, null, Enumerable.Empty<IConfigExtension>())
    {
    }

    private EventTypeMapping(
        Type eventType, EventApplier<TState>? eventApplier, IEnumerable<IConfigExtension> configExtensions)
    {
        EventType = eventType;
        EventApplier = eventApplier;
        ConfigExtensions = configExtensions;
    }

    public Type EventType { get; }
    public EventApplier<TState>? EventApplier { get; }

    public IEnumerable<IConfigExtension> ConfigExtensions { get; }

    public Builder ToBuilder() => new(this);

    public sealed class Builder
    {
        private readonly Type _eventType;
        private EventApplier<TState>? _eventApplier;

        internal Builder(EventTypeMapping<TState> mapping)
        {
            _eventType = mapping.EventType;
            EventApplier = mapping.EventApplier;
            ConfigExtensions = mapping.ConfigExtensions.ToList();
        }

        public EventApplier<TState>? EventApplier
        {
            get => _eventApplier;
            set
            {
                if (value != null && !value.EventType.IsAssignableFrom(_eventType))
                {
                    throw new ArgumentException(
                        $"Invalid event applier. Applier's event type '{value.EventType}' " +
                        $"is not assignable from '{_eventType}'.",
                        nameof(value));
                }

                _eventApplier = value;
            }
        }

        public List<IConfigExtension> ConfigExtensions { get; }

        public EventTypeMapping<TState> Build() => new(_eventType, EventApplier, ConfigExtensions);
    }
}