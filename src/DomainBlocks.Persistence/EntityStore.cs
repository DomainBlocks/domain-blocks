using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.Abstractions;
using DomainBlocks.Persistence.Entities;

namespace DomainBlocks.Persistence;

public sealed class EntityStore : IEntityStore
{
    private readonly IEventStore _eventStore;
    private readonly EntityAdapterRegistry _entityAdapterRegistry;
    private readonly EventMapper _eventMapper;
    private readonly EntityStoreConfig _config;
    private readonly ConditionalWeakTable<object, TrackedEntityContext> _trackedEntities = new();

    public EntityStore(EntityStoreConfig config)
    {
        _eventStore = config.EventStore;
        _entityAdapterRegistry = config.EntityAdapterRegistry;
        _eventMapper = config.EventMapper;
        _config = config;
    }

    public async Task<TEntity> LoadAsync<TEntity>(string entityId, CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        var streamName = GetStreamName<TEntity>(entityId);
        var readFromVersion = StreamVersion.Zero;

        var readEvents =
            _eventStore.ReadStreamAsync(streamName, StreamReadDirection.Forwards, readFromVersion, cancellationToken);

        var entityAdapter = GetEntityAdapter<TEntity>();
        var initialState = entityAdapter.CreateState(); // May come from a snapshot (in future).

        // Used in closure of TransformEventStream, so must be declared before the async enumerable is materialized,
        // i.e. before RestoreEntity is invoked.
        StreamVersion? loadedVersion = null;
        var loadedEventCount = 0;

        var entity = await entityAdapter.RestoreEntityAsync(initialState, TransformEventStream(), cancellationToken);
        var trackedEntityContext = new TrackedEntityContext(loadedVersion, loadedEventCount);

        // Track the entity so that the expected version will be known in a future call to SaveAsync.
        _trackedEntities.Add(entity, trackedEntityContext);

        return entity;

        async IAsyncEnumerable<object> TransformEventStream()
        {
            await foreach (var readEvent in readEvents)
            {
                loadedVersion = readEvent.StreamVersion;
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
        where TEntity : notnull
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

    private IEntityAdapter<TEntity> GetEntityAdapter<TEntity>() where TEntity : notnull
    {
        if (!_entityAdapterRegistry.TryGetFor<TEntity>(out var entityAdapter))
        {
            throw new ArgumentException($"Entity adapter not found for type '{typeof(TEntity)}'.", nameof(TEntity));
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