using System.Runtime.CompilerServices;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;

namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public static class EntityStore
{
    public static IEntityStore Create<TReadEvent, TWriteEvent, TRawData, TStreamVersion>(
        IEventStore<TReadEvent, TWriteEvent, TStreamVersion> eventStore,
        IEventAdapter<TReadEvent, TWriteEvent, TStreamVersion, TRawData> eventAdapter,
        EntityAdapterProvider entityAdapterProvider,
        EntityStoreOptions options, EntityStoreOptions<TRawData> dataOptions) where TStreamVersion : struct
    {
        return new EntityStore<TReadEvent, TWriteEvent, TStreamVersion, TRawData>(
            eventStore, eventAdapter, entityAdapterProvider, options, dataOptions);
    }
}

internal sealed class EntityStore<TReadEvent, TWriteEvent, TStreamVersion, TRawData> : IEntityStore
    where TStreamVersion : struct
{
    private readonly IEventStore<TReadEvent, TWriteEvent, TStreamVersion> _eventStore;
    private readonly IEventAdapter<TReadEvent, TWriteEvent, TStreamVersion, TRawData> _eventAdapter;
    private readonly EntityAdapterProvider _entityAdapterProvider;
    private readonly EntityStoreOptions _options;
    private readonly EntityStoreOptions<TRawData> _dataOptions;
    private readonly ConditionalWeakTable<object, TrackedEntityContext> _trackedEntities = new();

    public EntityStore(
        IEventStore<TReadEvent, TWriteEvent, TStreamVersion> eventStore,
        IEventAdapter<TReadEvent, TWriteEvent, TStreamVersion, TRawData> eventAdapter,
        EntityAdapterProvider entityAdapterProvider,
        EntityStoreOptions options,
        EntityStoreOptions<TRawData> dataOptions)
    {
        _eventStore = eventStore;
        _eventAdapter = eventAdapter;
        _entityAdapterProvider = entityAdapterProvider;
        _options = options;
        _dataOptions = dataOptions;
    }

    public async Task<TEntity> LoadAsync<TEntity>(string entityId, CancellationToken cancellationToken = default)
    {
        var (entity, context) = await RestoreAsync<TEntity>(entityId, cancellationToken);
        _trackedEntities.Add(entity!, context); // Why is ! required here?
        return entity;
    }

    public async Task SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        if (!_entityAdapterProvider.TryGetFor<TEntity>(out var entityAdapter))
        {
            throw new ArgumentException($"Entity adapter for '{entity.GetType()}' not found.", nameof(TEntity));
        }

        var raisedEvents = entityAdapter!.RaisedEventsSelector(entity).ToArray();
        if (raisedEvents.Length == 0) return;

        //object initialStateReplica;
        TStreamVersion? expectedVersion;
        int loadedEventCount;

        if (!_trackedEntities.TryGetValue(entity, out var context))
        {
            // Assume new
            //initialStateReplica = binding.StateFactory();
            expectedVersion = _eventStore.NoStreamVersion;
            loadedEventCount = 0;
        }
        else
        {
            //initialStateReplica = context.InitialStateReplica;
            loadedEventCount = context.LoadedEventCount;
            expectedVersion = context.StreamVersion;
        }

        // TODO: find an abstraction for this
        EventTypeMap eventTypeMap;
        int? snapshotEventCount;
        string streamIdPrefix;

        if (_options.StreamOptions.TryGetValue(typeof(TEntity), out var streamOptions))
        {
            eventTypeMap = streamOptions.EventTypeMap;
            snapshotEventCount = streamOptions.SnapshotEventCount;
            streamIdPrefix = streamOptions.StreamIdPrefix ?? DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }
        else
        {
            eventTypeMap = _options.EventTypeMap;
            snapshotEventCount = _options.SnapshotEventCount;
            streamIdPrefix = DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }

        var serializer = _dataOptions.StreamOptions.TryGetValue(typeof(TEntity), out var streamDataOptions)
            ? streamDataOptions.EventDataSerializer
            : _dataOptions.EventDataSerializer;

        var appendedWriteEvents = new List<TWriteEvent>();
        var eventCountSinceLastSnapshot = loadedEventCount;

        foreach (var e in raisedEvents)
        {
            var eventType = e.GetType();
            var eventTypeMapping = eventTypeMap[eventType];

            // TODO: Should we allow upcasting here? Is it permissible to emit an upcasted event?
            //entityReplicaRestorer.ApplyEvent(e);

            var eventName = eventTypeMapping.EventName;
            var rawData = serializer.Serialize(e);

            // TWriteEvent writeEvent;
            //
            // if (eventTypeMapping.MetadataFactory != null)
            // {
            //     var metadata = eventTypeMapping.MetadataFactory();
            //     var rawMetadata = serializer.Serialize(metadata);
            //     writeEvent = _eventAdapter.CreateWriteEvent(eventName, rawData, rawMetadata, serializer.ContentType);
            // }
            // else
            // {
            //     writeEvent = _eventAdapter.CreateWriteEvent(eventName, rawData, contentType: serializer.ContentType);
            // }

            var writeEvent = _eventAdapter.CreateWriteEvent(eventName, rawData, contentType: serializer.ContentType);

            appendedWriteEvents.Add(writeEvent);
            eventCountSinceLastSnapshot++;
            var isSnapshotRequired = eventCountSinceLastSnapshot >= snapshotEventCount;

            // if (!isSnapshotRequired) continue;
            //
            // var rawSnapshotData = serializer.Serialize(entityReplicaRestorer.CurrentState);
            //
            // var snapshotEvent = _eventAdapter.CreateWriteEvent(
            //     SystemEventNames.StateSnapshot, rawSnapshotData, contentType: serializer.ContentType);
            //
            // appendedWriteEvents.Add(snapshotEvent);
            // eventCountSinceLastSnapshot = 0;
        }

        var streamId = $"{streamIdPrefix}-{entityAdapter.IdSelector(entity)}";
        await _eventStore.AppendToStreamAsync(streamId, appendedWriteEvents, expectedVersion, cancellationToken);
    }

    private async Task<(TEntity, TrackedEntityContext)> RestoreAsync<TEntity>(
        string entityId, CancellationToken cancellationToken)
    {
        if (!_entityAdapterProvider.TryGetFor<TEntity>(out var entityAdapter))
        {
            throw new ArgumentException(null, nameof(TEntity));
        }

        // TODO: find an abstraction for this
        EventTypeMap eventTypeMap;
        string streamIdPrefix;

        if (_options.StreamOptions.TryGetValue(typeof(TEntity), out var streamOptions))
        {
            eventTypeMap = streamOptions.EventTypeMap;
            streamIdPrefix = streamOptions.StreamIdPrefix ?? DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }
        else
        {
            eventTypeMap = _options.EventTypeMap;
            streamIdPrefix = DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }

        var serializer = _dataOptions.StreamOptions.TryGetValue(typeof(TEntity), out var streamDataOptions)
            ? streamDataOptions.EventDataSerializer
            : _dataOptions.EventDataSerializer;

        object initialState;
        object initialStateReplica;
        TStreamVersion? streamVersion = null;
        var streamId = $"{streamIdPrefix}-{entityId}";

        var loadSnapshotResult = await TryLoadSnapshotAsync(streamId, entityAdapter!, serializer, cancellationToken);
        if (loadSnapshotResult != null)
        {
            initialState = loadSnapshotResult.State;
            initialStateReplica = loadSnapshotResult.StateReplica;
            streamVersion = loadSnapshotResult.StreamVersion;
        }
        else
        {
            initialState = entityAdapter!.StateFactory();
            initialStateReplica = entityAdapter.StateFactory();
        }

        var readEvents = _eventStore.ReadStreamAsync(
            ReadStreamDirection.Forwards, streamId, streamVersion, cancellationToken);

        var loadedEventCount = 0;
        var entity = await entityAdapter!.EntityRestorer(initialState, AdaptEventsStream(), cancellationToken);

        return (entity, new TrackedEntityContext(initialStateReplica, streamVersion, loadedEventCount));

        async IAsyncEnumerable<object> AdaptEventsStream()
        {
            await foreach (var readEvent in readEvents)
            {
                var eventName = _eventAdapter.GetEventName(readEvent);
                streamVersion = _eventAdapter.GetStreamVersion(readEvent);

                // Ignore any snapshot events. A snapshot event would usually be the first event in the case we start
                // reading from a snapshot event version, and there have been no other writes between reading the
                // snapshot and reading the events forward from the snapshot version. Writes could happen between these
                // two reads, which may result in additional snapshots.
                if (eventName == SystemEventNames.StateSnapshot) continue;

                loadedEventCount++;

                // TODO: Add test case for this scenario, ensuring we handle the version correctly.
                if (eventTypeMap.IsEventNameIgnored(eventName)) continue;

                var rawData = await _eventAdapter.GetEventData(readEvent);

                // TODO: Tidy up upcasting code
                var metadata = _eventAdapter.TryGetMetadata(readEvent, out var rawMetadata)
                    ? serializer.Deserialize<Dictionary<string, string>>(rawMetadata!)
                    : new Dictionary<string, string>();

                var eventType = eventTypeMap[eventName].EventType;
                var @event = serializer.Deserialize(rawData, eventType);

                // TODO: Upcasting
                // var eventTypeMapping = eventTypeMap[eventType];
                //
                // while (eventTypeMapping.EventUpcaster != null)
                // {
                //     @event = eventTypeMapping.EventUpcaster.Invoke(@event);
                //     eventTypeMapping = eventTypeMap[@event.GetType()];
                // }

                yield return @event;
            }
        }
    }

    private async Task<LoadSnapshotResult?> TryLoadSnapshotAsync<TEntity>(
        string streamId,
        EntityAdapter<TEntity> entityAdapter,
        IEventDataSerializer<TRawData> eventDataSerializer,
        CancellationToken cancellationToken)
    {
        // Read stream backwards until we find the first StateSnapshot event.
        var events = _eventStore.ReadStreamAsync(
            ReadStreamDirection.Backwards, streamId, cancellationToken: cancellationToken);

        await foreach (var e in events)
        {
            var eventName = _eventAdapter.GetEventName(e);
            var isSnapshotEvent = eventName == SystemEventNames.StateSnapshot;

            if (!isSnapshotEvent) continue;

            var rawData = await _eventAdapter.GetEventData(e);
            var state = eventDataSerializer.Deserialize(rawData, entityAdapter.StateType);
            var stateReplica = eventDataSerializer.Deserialize(rawData, entityAdapter.StateType);
            var streamVersion = _eventAdapter.GetStreamVersion(e);

            return new LoadSnapshotResult(state, stateReplica, streamVersion);
        }

        return null;
    }

    private class TrackedEntityContext
    {
        public TrackedEntityContext(
            object initialStateReplica,
            TStreamVersion? streamVersion,
            int loadedEventCount)
        {
            InitialStateReplica = initialStateReplica;
            StreamVersion = streamVersion;
            LoadedEventCount = loadedEventCount;
        }

        public object InitialStateReplica { get; }
        public TStreamVersion? StreamVersion { get; }
        public int LoadedEventCount { get; }
    }

    private class LoadSnapshotResult
    {
        public LoadSnapshotResult(object state, object stateReplica, TStreamVersion streamVersion)
        {
            State = state;
            StateReplica = stateReplica;
            StreamVersion = streamVersion;
        }

        public object State { get; }
        public object StateReplica { get; }
        public TStreamVersion StreamVersion { get; }
    }
}