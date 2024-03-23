using System.Runtime.CompilerServices;
using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Events;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence;

public sealed class EntityStore : IEntityStore
{
    private readonly IEventStore _eventStore;
    private readonly EntityAdapterProvider _entityAdapterProvider;
    private readonly EventMapper _eventMapper;
    private readonly EntityStoreConfig _config;
    private readonly ConditionalWeakTable<object, TrackedEntityContext> _trackedEntities = new();

    public EntityStore(EntityStoreConfig config)
    {
        _eventStore = config.EventStore;
        _entityAdapterProvider = config.EntityAdapterProvider;
        _eventMapper = config.EventMapper;
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

        StreamVersion? expectedVersion = null;
        var loadedEventCount = 0;

        if (_trackedEntities.TryGetValue(entity, out var context))
        {
            expectedVersion = context.StreamVersion;
            loadedEventCount = context.LoadedEventCount;
        }

        // TODO: find an abstraction for this
        int? snapshotEventCount;
        string streamIdPrefix;

        if (_config.StreamConfigs.TryGetValue(typeof(TEntity), out var streamConfig))
        {
            snapshotEventCount = streamConfig.SnapshotEventCount;
            streamIdPrefix = streamConfig.StreamIdPrefix ?? DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }
        else
        {
            snapshotEventCount = _config.SnapshotEventCount;
            streamIdPrefix = DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }

        var appendedWriteEvents = new List<WriteEvent>();
        var eventCountSinceLastSnapshot = loadedEventCount;

        foreach (var e in raisedEvents)
        {
            var writeEvent = _eventMapper.ToWriteEvent(e);
            appendedWriteEvents.Add(writeEvent);
            eventCountSinceLastSnapshot++;
            var isSnapshotRequired = eventCountSinceLastSnapshot >= snapshotEventCount;

            // TODO
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

        if (expectedVersion.HasValue)
        {
            await _eventStore.AppendToStreamAsync(
                streamId, appendedWriteEvents, expectedVersion.Value, cancellationToken);
        }
        else
        {
            await _eventStore.AppendToStreamAsync(
                streamId, appendedWriteEvents, ExpectedStreamState.NoStream, cancellationToken);
        }
    }

    private async Task<(TEntity, TrackedEntityContext)> RestoreAsync<TEntity>(
        string entityId, CancellationToken cancellationToken)
    {
        if (!_entityAdapterProvider.TryGetFor<TEntity>(out var entityAdapter))
        {
            throw new ArgumentException(null, nameof(TEntity));
        }

        // TODO: find an abstraction for this
        string streamIdPrefix;

        if (_config.StreamConfigs.TryGetValue(typeof(TEntity), out var streamConfig))
        {
            streamIdPrefix = streamConfig.StreamIdPrefix ?? DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }
        else
        {
            streamIdPrefix = DefaultStreamIdPrefix.CreateFor(typeof(TEntity));
        }

        object initialState;
        object initialStateReplica;
        var fromVersion = StreamVersion.Zero;
        var streamId = $"{streamIdPrefix}-{entityId}";
        var serializer = _eventMapper.Serializer;

        var loadSnapshotResult = await TryLoadSnapshotAsync(streamId, entityAdapter!, serializer, cancellationToken);
        if (loadSnapshotResult != null)
        {
            initialState = loadSnapshotResult.State;
            initialStateReplica = loadSnapshotResult.StateReplica;
            fromVersion = loadSnapshotResult.StreamVersion;
        }
        else
        {
            initialState = entityAdapter!.StateFactory();
            initialStateReplica = entityAdapter.StateFactory();
        }

        var readEvents = _eventStore.ReadStreamAsync(
            streamId, StreamReadDirection.Forwards, fromVersion, cancellationToken);

        StreamVersion? loadedVersion = null;
        var loadedEventCount = 0;
        var entity = await entityAdapter!.EntityRestorer(initialState, TransformEventStream(), cancellationToken);

        return (entity, new TrackedEntityContext(initialStateReplica, loadedVersion, loadedEventCount));

        async IAsyncEnumerable<object> TransformEventStream()
        {
            await foreach (var readEvent in readEvents)
            {
                loadedVersion = readEvent.StreamVersion;
                var eventName = readEvent.Name;

                // Ignore any snapshot events. A snapshot event would usually be the first event in the case we start
                // reading from a snapshot event version, and there have been no other writes between reading the
                // snapshot and reading the events forward from the snapshot version. Writes could happen between these
                // two reads, which may result in additional snapshots.
                if (eventName == SystemEventNames.StateSnapshot) continue;

                loadedEventCount++;

                // TODO: Add test case for this scenario, ensuring we handle the version correctly.
                // TODO: Can hide this behind EventMapper, i.e. it can return zero events if ignored.
                if (_eventMapper.IsEventNameIgnored(eventName)) continue;

                var deserializedEvents = _eventMapper.FromReadEvent(readEvent);
                foreach (var deserializedEvent in deserializedEvents)
                {
                    yield return deserializedEvent;
                }
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
            streamId, StreamReadDirection.Backwards, cancellationToken: cancellationToken);

        await foreach (var e in events)
        {
            var eventName = e.Name;
            var isSnapshotEvent = eventName == SystemEventNames.StateSnapshot;

            if (!isSnapshotEvent) continue;

            var state = serializer.Deserialize(e.Payload.Span, entityAdapter.StateType);
            var stateReplica = serializer.Deserialize(e.Payload.Span, entityAdapter.StateType);

            return new LoadSnapshotResult(state, stateReplica, e.StreamVersion);
        }

        return null;
    }

    private class TrackedEntityContext
    {
        public TrackedEntityContext(
            object initialStateReplica,
            StreamVersion? streamVersion,
            int loadedEventCount)
        {
            InitialStateReplica = initialStateReplica;
            StreamVersion = streamVersion;
            LoadedEventCount = loadedEventCount;
        }

        public object InitialStateReplica { get; }
        public StreamVersion? StreamVersion { get; }
        public int LoadedEventCount { get; }
    }

    private class LoadSnapshotResult
    {
        public LoadSnapshotResult(object state, object stateReplica, StreamVersion streamVersion)
        {
            State = state;
            StateReplica = stateReplica;
            StreamVersion = streamVersion;
        }

        public object State { get; }
        public object StateReplica { get; }
        public StreamVersion StreamVersion { get; }
    }
}