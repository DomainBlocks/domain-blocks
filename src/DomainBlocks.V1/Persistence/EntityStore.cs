using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Abstractions.Exceptions;
using DomainBlocks.V1.Persistence.Entities;

namespace DomainBlocks.V1.Persistence;

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
        return await LoadInternalAsync<TEntity>(entityId, throwIfStreamNotFound: true, cancellationToken);
    }
    
    public async Task<TEntity> CreateOrLoadAsync<TEntity>(string entityId,
        CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, throwIfStreamNotFound: false, cancellationToken);
    }

    private async Task<TEntity> LoadInternalAsync<TEntity>(string entityId, bool throwIfStreamNotFound = false,
    CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        var streamName = GetStreamName<TEntity>(entityId);
        var readFromPosition = StreamPosition.Start;

        var loadResult = await
            _eventStore.ReadStreamAsync(streamName, StreamReadDirection.Forwards, readFromPosition, cancellationToken);
        
        if (throwIfStreamNotFound && loadResult.LoadStatus == StreamLoadStatus.StreamNotFound)
        {
            throw new StreamNotFoundException($"Stream '{streamName}' could not be found.");
        }

        var entityAdapter = GetEntityAdapter<TEntity>();
        var initialState = entityAdapter.CreateState(); // May come from a snapshot (in future).

        // Used in closure of MapEventStream, so must be declared before the async enumerable is materialized, i.e.
        // before RestoreEntityAsync is invoked.
        StreamPosition? loadedVersion = null;
        var loadedEventCount = 0;

        var entity = await entityAdapter.RestoreEntityAsync(initialState, MapEventStream(), cancellationToken);
        var trackedEntityContext = new TrackedEntityContext(loadedVersion, loadedEventCount);

        // Track the entity so that the expected version will be known in a future call to SaveAsync.
        _trackedEntities.Add(entity, trackedEntityContext);

        return entity;

        async IAsyncEnumerable<object> MapEventStream()
        {
            await foreach (var eventRecord in loadResult.EventRecords.WithCancellation(cancellationToken))
            {
                loadedVersion = eventRecord.StreamPosition;
                loadedEventCount++;

                var mappedEvents = _eventMapper.ToEventObjects(eventRecord);
                foreach (var e in mappedEvents)
                {
                    yield return e;
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

        StreamPosition? expectedVersion = null;
        if (_trackedEntities.TryGetValue(entity, out var context))
        {
            expectedVersion = context.StreamVersion;
        }

        var entityId = entityAdapter.GetId(entity);
        var streamName = GetStreamName<TEntity>(entityId);
        var records = raisedEvents.Select(e => _eventMapper.ToWritableEventRecord(e));

        var appendTask = expectedVersion.HasValue
            ? _eventStore.AppendToStreamAsync(streamName, records, expectedVersion.Value, cancellationToken)
            : _eventStore.AppendToStreamAsync(streamName, records, ExpectedStreamState.NoStream, cancellationToken);

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
        public TrackedEntityContext(StreamPosition? streamVersion, int loadedEventCount)
        {
            StreamVersion = streamVersion;
            LoadedEventCount = loadedEventCount;
        }

        public StreamPosition? StreamVersion { get; }
        public int LoadedEventCount { get; }
    }
}