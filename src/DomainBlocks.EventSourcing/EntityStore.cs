using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public sealed class EntityStore(IEventStore eventStore, IEntityAdapterProvider entityAdapterProvider) : IEntityStore
{
    public async Task<Versioned<TEntity>> LoadAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default) where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, StreamNotFoundBehavior.Throw, cancellationToken);
    }

    public async Task<Versioned<TEntity>> LoadOrCreateAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default) where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, StreamNotFoundBehavior.CreateEntity, cancellationToken);
    }

    public async Task SaveAsync<TEntity>(Versioned<TEntity> entity, CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);

        var entityAdapter = GetEntityAdapter<TEntity>();
        var entityId = entityAdapter.GetId(entity.Entity);

        // PoC for adding metadata.
        var header = new NewEventHeader(
            metadata: [KeyValuePair.Create("EntityClrType", entity.Entity.GetType().Name)]);

        var uncommittedEvents = entityAdapter.GetUncommittedEvents(entity.Entity)
            .Select(e => NewEventRecord.Create(header, e))
            .ToArray();

        if (uncommittedEvents.Length == 0)
            return;

        var streamName = GetStreamName<TEntity>(entityId);
        var expectedState = ExpectedStreamState.FromVersion(entity.Version);

        await eventStore.AppendToStreamAsync(streamName, uncommittedEvents, expectedState, cancellationToken);
    }

    private async Task<Versioned<TEntity>> LoadInternalAsync<TEntity>(
        string entityId,
        StreamNotFoundBehavior streamNotFoundBehavior = StreamNotFoundBehavior.CreateEntity,
        CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        var streamName = GetStreamName<TEntity>(entityId);
        var result = await eventStore.ReadStreamAsync(streamName, cancellationToken: cancellationToken);

        if (streamNotFoundBehavior == StreamNotFoundBehavior.Throw && result.Status == ReadStreamStatus.StreamNotFound)
        {
            throw new StreamNotFoundException($"Stream '{streamName}' could not be found.");
        }

        var entityAdapter = GetEntityAdapter<TEntity>();
        var initialState = entityAdapter.CreateState(); // May come from a snapshot (in future).

        // Used in closure of EnumerateEvents, so must be declared before the async enumerable is materialized, i.e.
        // before RestoreEntityAsync is invoked.
        var loadedVersion = StreamVersion.None;

        var entity = await entityAdapter.RestoreAsync(initialState, EnumerateEvents(), cancellationToken);

        return Versioned.From(entity, loadedVersion);

        async IAsyncEnumerable<object> EnumerateEvents()
        {
            await foreach (var e in result.Events.WithCancellation(cancellationToken))
            {
                loadedVersion = e.Header.StreamVersion;
                yield return e.Payload;
            }
        }
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

    private enum StreamNotFoundBehavior
    {
        CreateEntity,
        Throw
    }
}