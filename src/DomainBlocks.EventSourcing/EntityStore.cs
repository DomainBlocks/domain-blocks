using System.Runtime.CompilerServices;
using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Persistence.Events;

namespace DomainBlocks.EventSourcing;

public sealed class EntityStore(
    IEventStore<object> eventStore,
    IEntityAdapterProvider entityAdapterProvider) : IEntityStore
{
    private readonly ConditionalWeakTable<object, TrackedEntityContext> _trackedEntities = new();

    public async Task<TEntity> LoadAsync<TEntity>(string entityId, CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, throwIfStreamNotFound: true, cancellationToken);
    }

    public async Task<TEntity> LoadOrCreateAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default) where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, throwIfStreamNotFound: false, cancellationToken);
    }

    private async Task<TEntity> LoadInternalAsync<TEntity>(
        string entityId,
        bool throwIfStreamNotFound = false,
        CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        var streamName = GetStreamName<TEntity>(entityId);
        var result = await eventStore.ReadStreamAsync(streamName, cancellationToken: cancellationToken);

        if (throwIfStreamNotFound && result.Status == ReadStreamStatus.StreamNotFound)
        {
            throw new StreamNotFoundException($"Stream '{streamName}' could not be found.");
        }

        var entityAdapter = GetEntityAdapter<TEntity>();
        var initialState = entityAdapter.CreateState(); // May come from a snapshot (in future).

        // Used in closure of EnumerateEvents, so must be declared before the async enumerable is materialized, i.e.
        // before RestoreEntityAsync is invoked.
        long loadedVersion = -1;

        var entity = await entityAdapter.RestoreAsync(initialState, EnumerateEvents(), cancellationToken);
        var trackedEntityContext = new TrackedEntityContext(loadedVersion);

        // Track the entity so that the expected version will be known in a future call to SaveAsync.
        _trackedEntities.Add(entity, trackedEntityContext);

        return entity;

        async IAsyncEnumerable<object> EnumerateEvents()
        {
            await foreach (var eventRecord in result.Events.WithCancellation(cancellationToken))
            {
                loadedVersion++;
                yield return eventRecord;
            }
        }
    }

    public async Task SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);

        var entityAdapter = GetEntityAdapter<TEntity>();

        var uncommittedEvents = entityAdapter.GetUncommittedEvents(entity).ToArray();
        if (uncommittedEvents.Length == 0)
            return;

        var expectedVersion = _trackedEntities.TryGetValue(entity, out var context) ? context.StreamVersion : -1;
        var entityId = entityAdapter.GetId(entity);
        var streamName = GetStreamName<TEntity>(entityId);

        await eventStore.AppendToStreamAsync(streamName, uncommittedEvents, expectedVersion, cancellationToken);
    }

    private IEntityAdapter<TEntity> GetEntityAdapter<TEntity>() where TEntity : notnull
    {
        return entityAdapterProvider.GetFor<TEntity>() ?? throw new ArgumentException(
            $"Entity adapter not found for type '{typeof(TEntity).GetPrettyName()}'.",
            nameof(TEntity));
    }

    private static string GetStreamName<TEntity>(string entityId)
    {
        var streamNamePrefix = DefaultStreamNamePrefix.CreateFor(typeof(TEntity));
        return $"{streamNamePrefix}-{entityId}";
    }

    public record TrackedEntityContext(long? StreamVersion);
}