using System.Collections.Concurrent;

namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public static class EventSourcedStateRepository
{
    public static EventSourcedStateRepositoryBuilder CreateBuilder() => new();

    internal static IEventSourcedStateRepository Create<TReadEvent, TWriteEvent, TRawData, TStreamVersion>(
        IEnumerable<IStateEventStreamBinding> bindings,
        StateEventStreamBindingDefaults<TRawData>? defaults,
        IEventRepository<TReadEvent, TWriteEvent, TStreamVersion> eventRepository,
        IEventAdapter<TReadEvent, TWriteEvent, TRawData, TStreamVersion> eventAdapter) where TStreamVersion : struct
    {
        return new EventSourcedStateRepository<TReadEvent, TWriteEvent, TRawData, TStreamVersion>(
            bindings, defaults, eventRepository, eventAdapter);
    }
}

internal sealed class EventSourcedStateRepository<TReadEvent, TWriteEvent, TRawData, TStreamVersion> :
    IEventSourcedStateRepository
    where TStreamVersion : struct
{
    private readonly ConcurrentDictionary<Type, IStateEventStreamBinding> _bindings;
    private readonly StateEventStreamBindingDefaults<TRawData>? _defaults;
    private readonly IEventRepository<TReadEvent, TWriteEvent, TStreamVersion> _eventRepository;
    private readonly IEventAdapter<TReadEvent, TWriteEvent, TRawData, TStreamVersion> _eventAdapter;

    public EventSourcedStateRepository(
        IEnumerable<IStateEventStreamBinding> bindings,
        StateEventStreamBindingDefaults<TRawData>? defaults,
        IEventRepository<TReadEvent, TWriteEvent, TStreamVersion> eventRepository,
        IEventAdapter<TReadEvent, TWriteEvent, TRawData, TStreamVersion> eventAdapter)
    {
        var bindingKeyValuePairs = bindings.Select(x => KeyValuePair.Create(x.StateType, x));
        _bindings = new ConcurrentDictionary<Type, IStateEventStreamBinding>(bindingKeyValuePairs);
        _defaults = defaults;
        _eventRepository = eventRepository;
        _eventAdapter = eventAdapter;
    }

    public (TState State, IStateEventStreamAppender Appender) New<TState>(string id)
    {
        var binding = GetBinding<TState>();
        var newState = binding.StateFactory();
        var newStateReplica = binding.StateFactory();
        var streamId = binding.GetStreamId(id);

        var appender = new StateEventStreamAppender<TState, TWriteEvent, TRawData, TStreamVersion>(
            newStateReplica,
            0,
            streamId,
            _eventRepository.NoStreamVersion,
            _eventRepository,
            _eventAdapter,
            binding);

        return (newState, appender);
    }

    public async Task<(TState State, IStateEventStreamAppender Appender)> RestoreAsync<TState>(
        string id, CancellationToken cancellationToken = default)
    {
        var binding = GetBinding<TState>();
        var streamId = binding.GetStreamId(id);
        var result = await RestoreAsync(streamId, binding, cancellationToken);

        var appender = new StateEventStreamAppender<TState, TWriteEvent, TRawData, TStreamVersion>(
            result.StateReplica,
            result.LoadedEventCount,
            streamId,
            result.StreamVersion,
            _eventRepository,
            _eventAdapter,
            binding);

        return (result.State, appender);
    }

    public async Task<TState> ReadOnlyRestoreAsync<TState>(string id, CancellationToken cancellationToken = default)
    {
        var binding = GetBinding<TState>();
        var streamId = binding.GetStreamId(id);
        var result = await RestoreAsync(streamId, binding, cancellationToken);
        return result.State;
    }

    private StateEventStreamBinding<TState, TRawData> GetBinding<TState>()
    {
        var stateType = typeof(TState);
        IStateEventStreamBinding? binding;

        if (_defaults != null)
        {
            binding = _bindings.GetOrAdd(stateType, _ =>
            {
                var builder = StateEventStreamBinding.CreateBuilder<TState, TRawData>();
                _defaults.ApplyTo(builder);
                builder.EventTypes.MapAll();
                return builder.Build();
            });
        }
        else if (!_bindings.TryGetValue(stateType, out binding))
        {
            throw new InvalidOperationException(
                $"Binding for type '{stateType}' not found, and dynamic configuration is not enabled.");
        }

        return (StateEventStreamBinding<TState, TRawData>)binding;
    }

    private async Task<RestoreResult<TState>> RestoreAsync<TState>(
        string streamId,
        StateEventStreamBinding<TState, TRawData> binding,
        CancellationToken cancellationToken)
    {
        LoadSnapshotResult<TState>? loadSnapshotResult = null;
        if (binding.SnapshotEventCount.HasValue)
        {
            loadSnapshotResult = await LoadSnapshotAsync(streamId, binding, cancellationToken);
        }

        var state = loadSnapshotResult == null ? binding.StateFactory() : loadSnapshotResult.State;
        var stateReplica = loadSnapshotResult == null ? binding.StateFactory() : loadSnapshotResult.StateReplica;
        var streamVersion = loadSnapshotResult?.StreamVersion;

        var events = _eventRepository.ReadStreamAsync(
            ReadStreamDirection.Forwards, streamId, streamVersion, cancellationToken);

        var loadedEventCount = 0;
        var eventTypes = binding.EventTypes;
        var serializer = binding.EventDataSerializer;

        await foreach (var e in events)
        {
            var eventName = _eventAdapter.GetEventName(e);
            streamVersion = _eventAdapter.GetStreamVersion(e);

            // Ignore any snapshot events. A snapshot event would usually be the first event in the case we start
            // reading from a snapshot event version, and there have been no other writes between reading the snapshot
            // and reading the events forward from the snapshot version. Writes could happen between these two reads,
            // which may result in additional snapshots.
            if (eventName == SystemEventNames.StateSnapshot) continue;

            loadedEventCount++;

            // TODO: Add test case for this scenario, ensuring we handle the version correctly.
            if (eventTypes.IsEventNameIgnored(eventName)) continue;

            var rawData = await _eventAdapter.GetEventData(e);
            var rawMetadata = _eventAdapter.GetEventMetadata(e);

            // TODO: Tidy up upcasting code
            var metadata = EqualityComparer<TRawData>.Default.Equals(rawMetadata, default)
                ? new Dictionary<string, string>()
                : serializer.Deserialize<Dictionary<string, string>>(rawMetadata!);

            var eventType = eventTypes.ResolveEventType(eventName, metadata);
            var @event = serializer.Deserialize(rawData, eventType);
            var eventTypeMapping = eventTypes[eventType];

            while (eventTypeMapping.EventUpcaster != null)
            {
                @event = eventTypeMapping.EventUpcaster.Invoke(@event);
                eventTypeMapping = eventTypes[@event.GetType()];
            }

            var eventApplier = eventTypeMapping.EventApplier!;
            state = eventApplier.Invoke(state, @event);
            stateReplica = eventApplier.Invoke(stateReplica, @event);
        }

        return new RestoreResult<TState>(state, stateReplica, streamVersion, loadedEventCount);
    }

    private async Task<LoadSnapshotResult<TState>?> LoadSnapshotAsync<TState>(
        string streamId,
        StateEventStreamBinding<TState, TRawData> binding,
        CancellationToken cancellationToken)
    {
        // Read stream backwards until we find the first StateSnapshot event.
        var events = _eventRepository.ReadStreamAsync(
            ReadStreamDirection.Backwards, streamId, cancellationToken: cancellationToken);

        await foreach (var e in events)
        {
            var eventName = _eventAdapter.GetEventName(e);
            var isSnapshotEvent = eventName == SystemEventNames.StateSnapshot;

            if (!isSnapshotEvent) continue;

            var rawData = await _eventAdapter.GetEventData(e);
            var state = binding.EventDataSerializer.Deserialize<TState>(rawData);
            var stateReplica = binding.EventDataSerializer.Deserialize<TState>(rawData);
            var streamVersion = _eventAdapter.GetStreamVersion(e);

            return new LoadSnapshotResult<TState>(state, stateReplica, streamVersion);
        }

        return null;
    }

    private class RestoreResult<TState>
    {
        public RestoreResult(TState state, TState stateReplica, TStreamVersion? streamVersion, int loadedEventCount)
        {
            State = state;
            StateReplica = stateReplica;
            StreamVersion = streamVersion;
            LoadedEventCount = loadedEventCount;
        }

        public TState State { get; }
        public TState StateReplica { get; }
        public TStreamVersion? StreamVersion { get; }
        public int LoadedEventCount { get; }
    }

    private class LoadSnapshotResult<TState>
    {
        public LoadSnapshotResult(TState state, TState stateReplica, TStreamVersion streamVersion)
        {
            State = state;
            StateReplica = stateReplica;
            StreamVersion = streamVersion;
        }

        public TState State { get; }
        public TState StateReplica { get; }
        public TStreamVersion StreamVersion { get; }
    }
}