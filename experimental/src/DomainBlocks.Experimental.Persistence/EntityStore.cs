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
        if (!_entityAdapterProvider.TryGetFor<TEntity>(out var entityAdapter))
        {
            throw new ArgumentException(null, nameof(TEntity));
        }

        var streamNamePrefix = _config.StreamConfigs.TryGetValue(typeof(TEntity), out var streamConfig)
            ? streamConfig.StreamNamePrefix ?? DefaultStreamNamePrefix.CreateFor(typeof(TEntity))
            : DefaultStreamNamePrefix.CreateFor(typeof(TEntity));

        var initialState = entityAdapter!.StateFactory();
        var fromVersion = StreamVersion.Zero;
        var streamId = $"{streamNamePrefix}-{entityId}";

        var readEvents = _eventStore.ReadStreamAsync(
            streamId, StreamReadDirection.Forwards, fromVersion, cancellationToken);

        StreamVersion? loadedVersion = null;
        var loadedEventCount = 0;
        var entity = await entityAdapter.EntityRestorer(initialState, TransformEventStream(), cancellationToken);

        _trackedEntities.Add(entity!, new TrackedEntityContext(loadedVersion, loadedEventCount));

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

    public async Task SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        if (!_entityAdapterProvider.TryGetFor<TEntity>(out var entityAdapter))
        {
            throw new ArgumentException($"Entity adapter for '{entity.GetType()}' not found.", nameof(TEntity));
        }

        Debug.Assert(entityAdapter != null);

        var raisedEvents = entityAdapter.RaisedEventsSelector(entity).ToArray();
        if (raisedEvents.Length == 0) return;

        StreamVersion? expectedVersion = null;

        if (_trackedEntities.TryGetValue(entity, out var context))
        {
            expectedVersion = context.StreamVersion;
        }

        var streamNamePrefix = _config.StreamConfigs.TryGetValue(typeof(TEntity), out var streamConfig)
            ? streamConfig.StreamNamePrefix ?? DefaultStreamNamePrefix.CreateFor(typeof(TEntity))
            : DefaultStreamNamePrefix.CreateFor(typeof(TEntity));

        var streamName = $"{streamNamePrefix}-{entityAdapter.IdSelector(entity)}";
        var writeEvents = raisedEvents.Select(e => _eventMapper.ToWriteEvent(e));

        if (expectedVersion.HasValue)
        {
            await _eventStore.AppendToStreamAsync(
                streamName, writeEvents, expectedVersion.Value, cancellationToken);
        }
        else
        {
            await _eventStore.AppendToStreamAsync(
                streamName, writeEvents, ExpectedStreamState.NoStream, cancellationToken);
        }
    }

    private class TrackedEntityContext
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