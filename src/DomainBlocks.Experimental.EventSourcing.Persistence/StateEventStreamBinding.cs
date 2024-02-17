using System.Linq.Expressions;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public static class StateEventStreamBinding
{
    public static StateEventStreamBinding<TState, TRawData>.Builder CreateBuilder<TState, TRawData>()
    {
        return StateEventStreamBinding<TState, TRawData>.Empty.ToBuilder();
    }
}

public sealed class StateEventStreamBinding<TState, TRawData> : IStateEventStreamBinding
{
    private static readonly Lazy<Func<TState>> DefaultStateFactory = new(() => GetDefaultStateFactory());
    internal static readonly StateEventStreamBinding<TState, TRawData> Empty = new();

    private readonly EventTypeMap<TState> _originalEventTypeMap;
    private readonly IEventDataSerializer<TRawData>? _eventDataSerializer;

    private StateEventStreamBinding() : this(
        DefaultStateFactory.Value,
        GetDefaultStreamIdPrefix(),
        EventTypeMap<TState>.Empty,
        null,
        null)
    {
    }

    private StateEventStreamBinding(
        Func<TState> stateFactory,
        string streamIdPrefix,
        EventTypeMap<TState> eventTypes,
        IEventDataSerializer<TRawData>? eventDataSerializer,
        int? snapshotEventCount)
    {
        StateFactory = stateFactory;
        StreamIdPrefix = streamIdPrefix;
        _originalEventTypeMap = eventTypes;
        EventTypes = eventTypes.Extend();
        _eventDataSerializer = eventDataSerializer;
        SnapshotEventCount = snapshotEventCount;
    }

    public Type StateType { get; } = typeof(TState);
    internal Func<TState> StateFactory { get; }
    private string StreamIdPrefix { get; }
    internal ExtendedEventTypeMap<TState> EventTypes { get; }

    internal IEventDataSerializer<TRawData> EventDataSerializer =>
        _eventDataSerializer ?? throw new InvalidOperationException("Event data serializer not configured.");

    internal int? SnapshotEventCount { get; }

    internal string GetStreamId(string id) => $"{StreamIdPrefix}-{id}";

    public Builder ToBuilder() => new(this);

    private static Func<TState> GetDefaultStateFactory()
    {
        var ctor = typeof(TState).GetConstructor(Type.EmptyTypes);
        if (ctor == null)
        {
            return () => throw new InvalidOperationException(
                $"Unable to instantiate '{typeof(TState)}'. " +
                $"No factory function specified, and no default constructor found.");
        }

        var newExpr = Expression.New(ctor);
        var lambda = Expression.Lambda<Func<TState>>(newExpr);
        return lambda.Compile();
    }

    private static string GetDefaultStreamIdPrefix()
    {
        var name = typeof(TState).Name;
        return $"{name[..1].ToLower()}{name[1..]}";
    }

    public class Builder : IStateEventStreamBindingBuilder
    {
        private Func<TState> _stateFactory;
        private string _streamIdPrefix;
        private IEventDataSerializer<TRawData>? _eventDataSerializer;
        private int? _snapshotEventCount;

        internal Builder(StateEventStreamBinding<TState, TRawData> binding)
        {
            _stateFactory = binding.StateFactory;
            _streamIdPrefix = binding.StreamIdPrefix;
            _eventDataSerializer = binding._eventDataSerializer;
            _snapshotEventCount = binding.SnapshotEventCount;

            EventTypes = binding._originalEventTypeMap.ToBuilder();
        }

        public EventTypeMap<TState>.Builder EventTypes { get; }

        public Builder SetStateFactory(Func<TState> factory)
        {
            _stateFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public Builder SetStreamIdPrefix(string streamIdPrefix)
        {
            if (string.IsNullOrWhiteSpace(streamIdPrefix))
                throw new ArgumentException("Stream ID prefix cannot be null or whitespace.", nameof(streamIdPrefix));

            _streamIdPrefix = streamIdPrefix;
            return this;
        }

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

        public StateEventStreamBinding<TState, TRawData> Build()
        {
            var state = _stateFactory();

            if (state is IEventTypeMapSource<TState> eventTypeMapSource)
            {
                EventTypes.Merge(eventTypeMapSource.EventTypeMap);
            }

            var builtEventTypes = EventTypes.Build();

            return new StateEventStreamBinding<TState, TRawData>(
                _stateFactory,
                _streamIdPrefix,
                builtEventTypes,
                _eventDataSerializer,
                _snapshotEventCount);
        }

        IStateEventStreamBinding IStateEventStreamBindingBuilder.Build() => Build();
    }
}