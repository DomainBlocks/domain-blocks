using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public sealed class StateEventStreamBindingDefaults<TRawData>
{
    internal static readonly StateEventStreamBindingDefaults<TRawData> Default = new();

    private StateEventStreamBindingDefaults()
    {
        EventTypes = EventTypeMapDefaults.Default;

        // Use JSON serialization by default for known raw data types.
        if (typeof(TRawData) == typeof(ReadOnlyMemory<byte>))
        {
            EventDataSerializer = (IEventDataSerializer<TRawData>)new JsonBytesEventDataSerializer();
        }
        else if (typeof(TRawData) == typeof(string))
        {
            EventDataSerializer = (IEventDataSerializer<TRawData>)new JsonStringEventDataSerializer();
        }
    }

    private StateEventStreamBindingDefaults(
        EventTypeMapDefaults eventTypes,
        IEventDataSerializer<TRawData>? eventDataSerializer,
        int? snapshotEventCount)
    {
        EventTypes = eventTypes;
        EventDataSerializer = eventDataSerializer;
        SnapshotEventCount = snapshotEventCount;
    }

    public EventTypeMapDefaults EventTypes { get; }
    public IEventDataSerializer<TRawData>? EventDataSerializer { get; }
    public int? SnapshotEventCount { get; }

    public void ApplyTo<TState>(StateEventStreamBinding<TState, TRawData>.Builder builder)
    {
        EventTypes.ApplyTo(builder.EventTypes);

        if (EventDataSerializer != null)
        {
            builder.SetEventDataSerializer(EventDataSerializer);
        }

        builder.SetSnapshotEventCount(SnapshotEventCount);
    }

    internal Builder ToBuilder() => new(this);

    public sealed class Builder
    {
        private IEventDataSerializer<TRawData>? _eventDataSerializer;
        private int? _snapshotEventCount;

        internal Builder(StateEventStreamBindingDefaults<TRawData> config)
        {
            _eventDataSerializer = config.EventDataSerializer;
            _snapshotEventCount = config.SnapshotEventCount;

            EventTypes = config.EventTypes.ToBuilder();
        }

        public EventTypeMapDefaults.Builder EventTypes { get; }

        public Builder SetEventDataSerializer(IEventDataSerializer<TRawData> serializer)
        {
            _eventDataSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            return this;
        }

        public Builder SetSnapshotEventCount(int? eventCount)
        {
            _snapshotEventCount = eventCount;
            return this;
        }

        internal StateEventStreamBindingDefaults<TRawData> Build() =>
            new(EventTypes.Build(), _eventDataSerializer, _snapshotEventCount);
    }
}