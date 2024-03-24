using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Events;

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
        var streamName = GetStreamName<TEntity>(entityId);
        var fromVersion = StreamVersion.Zero;

        var readEvents =
            _eventStore.ReadStreamAsync(streamName, StreamReadDirection.Forwards, fromVersion, cancellationToken);

        var entityAdapter = GetEntityAdapter<TEntity>();
        var initialState = entityAdapter.CreateState();

        // Used in closure of TransformEventStream, so must be declared before the async enumerable is materialized,
        // i.e. before RestoreEntity is invoked.
        StreamVersion? loadedVersion = null;
        var loadedEventCount = 0;

        var entity = await entityAdapter.RestoreEntity(initialState, TransformEventStream(), cancellationToken);
        var trackedEntityContext = new TrackedEntityContext(loadedVersion, loadedEventCount);

        // Track the entity so that the expected version will be known in a future call to SaveAsync.
        _trackedEntities.Add(entity!, trackedEntityContext);

        return entity;

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

                var transformedEvents = _eventMapper.FromReadEvent(readEvent);
                foreach (var deserializedEvent in transformedEvents)
                {
                    yield return deserializedEvent;
                }
            }
        }
    }

    public async Task SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        var entityAdapter = GetEntityAdapter<TEntity>();

        var raisedEvents = entityAdapter.GetRaisedEvents(entity).ToArray();
        if (raisedEvents.Length == 0) return;

        StreamVersion? expectedVersion = null;
        if (_trackedEntities.TryGetValue(entity, out var context))
        {
            expectedVersion = context.StreamVersion;
        }

        var entityId = entityAdapter.GetId(entity);
        var streamName = GetStreamName<TEntity>(entityId);
        var writeEvents = raisedEvents.Select(e => _eventMapper.ToWriteEvent(e));

        var appendTask = expectedVersion.HasValue
            ? _eventStore.AppendToStreamAsync(streamName, writeEvents, expectedVersion.Value, cancellationToken)
            : _eventStore.AppendToStreamAsync(streamName, writeEvents, ExpectedStreamState.NoStream, cancellationToken);

        await appendTask;
    }

    private IEntityAdapter<TEntity> GetEntityAdapter<TEntity>()
    {
        if (!_entityAdapterProvider.TryGetFor<TEntity>(out var entityAdapter))
        {
            throw new ArgumentException(null, nameof(TEntity));
        }

        Debug.Assert(entityAdapter != null, nameof(entityAdapter) + " != null");

        return entityAdapter;
    }

    private string GetStreamName<TEntity>(string entityId)
    {
        var streamNamePrefix = _config.StreamConfigs.TryGetValue(typeof(TEntity), out var streamConfig)
            ? streamConfig.StreamNamePrefix ?? DefaultStreamNamePrefix.CreateFor(typeof(TEntity))
            : DefaultStreamNamePrefix.CreateFor(typeof(TEntity));

        return $"{streamNamePrefix}-{entityId}";
    }

    private sealed class TrackedEntityContext
    {
        public TrackedEntityContext(StreamVersion? streamVersion, int loadedEventCount)
        {
            StreamVersion = streamVersion;
            LoadedEventCount = loadedEventCount;
        }

        public StreamVersion? StreamVersion { get; }
        public int LoadedEventCount { get; }
    }
}