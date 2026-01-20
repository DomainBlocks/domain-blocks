namespace DomainBlocks.EventStore.TypeMapping;

/// <summary>
/// Defines the mapping between event CLR types and their string names used in storage.
/// </summary>
/// <remarks>
/// <para>
/// Type-to-name mappings are used when writing events, and must be one-to-one.
/// </para>
/// <para>
/// Name-to-type mappings are used when reading events, and may be many-to-one to support renamed events, or to
/// deserialize events with a shared structure into a common CLR type.
/// </para>
/// </remarks>
public class EventTypeMap
{
    private EventTypeMap(AppendEventTypeMap appends, ReadEventTypeMap reads)
    {
        Appends = appends;
        Reads = reads;
    }

    public AppendEventTypeMap Appends { get; }
    public ReadEventTypeMap Reads { get; }

    public static EventTypeMap Create(Action<Builder> configure)
    {
        var builder = new Builder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Builds an <see cref="EventTypeMap"/> by registering mappings between event CLR types and string names.
    /// </summary>
    public class Builder
    {
        private readonly AppendEventTypeMap.Builder _appendMapBuilder = new();
        private readonly ReadEventTypeMap.Builder _readMapBuilder = new();

        internal Builder()
        {
        }

        public Builder MapType<TEvent>(Action<Mapping>? configure = null)
        {
            var mapping = new Mapping(typeof(TEvent));
            configure?.Invoke(mapping);

            _appendMapBuilder.MapType<TEvent>(m => m.ToName(mapping.EventName));
            _readMapBuilder.MapType<TEvent>(m => m.FromNames(mapping.EventName));

            return this;
        }

        public Builder ForAppends(Action<AppendEventTypeMap.Builder> configure)
        {
            configure(_appendMapBuilder);
            return this;
        }

        public Builder ForReads(Action<ReadEventTypeMap.Builder> configure)
        {
            configure(_readMapBuilder);
            return this;
        }

        internal EventTypeMap Build() => new(_appendMapBuilder.Build(), _readMapBuilder.Build());

        public sealed class Mapping
        {
            internal Mapping(Type eventType)
            {
                EventName = eventType.Name;
            }

            public void WithName(string name)
            {
                EventName = name;
            }

            internal string EventName { get; private set; }
        }
    }
}