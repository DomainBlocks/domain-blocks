using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Persistence.Events;

namespace DomainBlocks.EventSourcing;

public sealed class EntityStore(IEventStore eventStore, IEntityAdapterProvider entityAdapterProvider) : IEntityStore
{
    public async Task<Versioned<TEntity>> LoadAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default) where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, throwIfStreamNotFound: true, cancellationToken);
    }

    public async Task<Versioned<TEntity>> LoadOrCreateAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default) where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, throwIfStreamNotFound: false, cancellationToken);
    }

    private async Task<Versioned<TEntity>> LoadInternalAsync<TEntity>(
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

        return Versioned.From(entity, loadedVersion);

        async IAsyncEnumerable<object> EnumerateEvents()
        {
            await foreach (var @event in result.Events.WithCancellation(cancellationToken))
            {
                loadedVersion++;
                yield return @event;
            }
        }
    }

    public async Task SaveAsync<TEntity>(Versioned<TEntity> entity, CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);

        var entityAdapter = GetEntityAdapter<TEntity>();

        var uncommittedEvents = entityAdapter.GetUncommittedEvents(entity.Entity).ToArray();
        if (uncommittedEvents.Length == 0)
            return;

        var entityId = entityAdapter.GetId(entity.Entity);
        var streamName = GetStreamName<TEntity>(entityId);

        await eventStore.AppendToStreamAsync(streamName, uncommittedEvents, entity.ExpectedVersion, cancellationToken);
    }

    private IEntityAdapter<TEntity> GetEntityAdapter<TEntity>() where TEntity : notnull
    {
        return entityAdapterProvider.GetAdapter<TEntity>() ?? throw new ArgumentException(
            $"Entity adapter not found for type '{typeof(TEntity)}'.",
            nameof(TEntity));
    }

    private static string GetStreamName<TEntity>(string entityId)
    {
        var streamNamePrefix = DefaultStreamNamePrefix.CreateFor(typeof(TEntity));
        return $"{streamNamePrefix}-{entityId}";
    }
}