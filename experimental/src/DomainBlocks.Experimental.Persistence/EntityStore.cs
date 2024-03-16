using System.Runtime.CompilerServices;
using DomainBlocks.Experimental.Persistence.Adapters;
using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence;

public static class EntityStore
{
    public static IEntityStore Create<TReadEvent, TWriteEvent, TStreamVersion>(
        IEventStore<TReadEvent, TWriteEvent, TStreamVersion> eventStore,
        IEventAdapter<TReadEvent, TWriteEvent, TStreamVersion> eventAdapter,
        EntityAdapterProvider entityAdapterProvider,
        EntityStoreConfig config) where TStreamVersion : struct
    {
        return new EntityStore<TReadEvent, TWriteEvent, TStreamVersion>(
            eventStore, eventAdapter, entityAdapterProvider, config);
    }
}

internal sealed class EntityStore<TReadEvent, TWriteEvent, TStreamVersion> : IEntityStore where TStreamVersion : struct
{
    private readonly IEventStore<TReadEvent, TWriteEvent, TStreamVersion> _eventStore;
    private readonly IEventAdapter<TReadEvent, TWriteEvent, TStreamVersion> _eventAdapter;
    private readonly EntityAdapterProvider _entityAdapterProvider;
    private readonly EntityStoreConfig _config;
    private readonly ConditionalWeakTable<object, TrackedEntityContext> _trackedEntities = new();

    public EntityStore(
        IEventStore<TReadEvent, TWriteEvent, TStreamVersion> eventStore,
        IEventAdapter<TReadEvent, TWriteEvent, TStreamVersion> eventAdapter,
        EntityAdapterProvider entityAdapterProvider,
        EntityStoreConfig config)
    {
        _eventStore = eventStore;
        _eventAdapter = eventAdapter;
        _entityAdapterProvider = entityAdapterProvider;
        _config = config;
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
        IEventDataSerializer serializer;
        int? snapshotEventCount;
        string streamIdPrefix;

        if (_config.StreamConfigs.TryGetValue(typeof(TEntity), out var streamConfig))
        {
            eventTypeMap = streamConfig.EventTypeMap;
            serializer = streamConfig.EventDataSerializer ?? _config.EventDataSerializer;
            snapshotEventCount = streamConfig.SnapshotEventCount;
            streamIdPrefix = streamConfig.StreamIdPrefix ?? DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }
        else
        {
            eventTypeMap = _config.EventTypeMap;
            serializer = _config.EventDataSerializer;
            snapshotEventCount = _config.SnapshotEventCount;
            streamIdPrefix = DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }

        var appendedWriteEvents = new List<TWriteEvent>();
        var eventCountSinceLastSnapshot = loadedEventCount;

        foreach (var e in raisedEvents)
        {
            var eventType = e.GetType();
            var eventTypeMapping = eventTypeMap[eventType];

            // TODO: Should we allow upcasting here? Is it permissible to emit an upcasted event?
            //entityReplicaRestorer.ApplyEvent(e);

            var eventName = eventTypeMapping.EventName;
            //var rawData = serializer.Serialize(e);

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

            //var writeEvent = _eventAdapter.CreateWriteEvent(eventName, rawData, contentType: serializer.ContentType);
            var writeEvent = _eventAdapter.CreateWriteEvent(eventName, e, null, serializer);

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
        IEventDataSerializer serializer;
        string streamIdPrefix;

        if (_config.StreamConfigs.TryGetValue(typeof(TEntity), out var streamConfig))
        {
            eventTypeMap = streamConfig.EventTypeMap;
            serializer = streamConfig.EventDataSerializer ?? _config.EventDataSerializer;
            streamIdPrefix = streamConfig.StreamIdPrefix ?? DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }
        else
        {
            eventTypeMap = _config.EventTypeMap;
            serializer = _config.EventDataSerializer;
            streamIdPrefix = DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }

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
        var entity = await entityAdapter!.EntityRestorer(initialState, TransformEventsStream(), cancellationToken);

        return (entity, new TrackedEntityContext(initialStateReplica, streamVersion, loadedEventCount));

        async IAsyncEnumerable<object> TransformEventsStream()
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

                var eventType = eventTypeMap[eventName].EventType;
                var @event = await _eventAdapter.Deserialize(readEvent, eventType, serializer);

                yield return @event;
            }
        }
    }

    private async Task<LoadSnapshotResult?> TryLoadSnapshotAsync<TEntity>(
        string streamId,
        EntityAdapter<TEntity> entityAdapter,
        IEventDataSerializer serializer,
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

            var state = await _eventAdapter.Deserialize(e, entityAdapter.StateType, serializer);
            var stateReplica = await _eventAdapter.Deserialize(e, entityAdapter.StateType, serializer);
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