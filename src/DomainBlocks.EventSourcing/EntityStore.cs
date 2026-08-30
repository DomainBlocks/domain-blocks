/*
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public sealed class EntityStore<TEvent, TStreamId, TStreamPos, TLogPos>(
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
    IEntityDefinitionProvider<TEvent> entityDefinitionProvider) :
    IEntityStore
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public async Task<Versioned<TEntity>> LoadAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default) where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, StreamNotFoundBehavior.Throw, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Versioned<TEntity>> LoadOrCreateAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default) where TEntity : notnull
    {
        return await LoadInternalAsync<TEntity>(entityId, StreamNotFoundBehavior.Ignore, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync<TEntity>(Versioned<TEntity> entity, CancellationToken cancellationToken = default)
        where TEntity : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);

        var entityDefinition = GetEntityDefinition<TEntity>();
        var entityId = entityDefinition.GetId(entity.Entity);

        // PoC for adding metadata.
        // Should this be stream-level metadata?
        KeyValuePair<string, string>[] metadata = [KeyValuePair.Create("EntityClrType", entity.Entity.GetType().Name)];

        var uncommittedEvents = entityDefinition.GetUncommittedEvents(entity.Entity)
            .Select(e => AppendableEvent.Create(e, metadata))
            .ToArray();

        if (uncommittedEvents.Length == 0)
            return;

        var streamName = GetStreamName<TEntity>(entityId);

        var expectedStreamState = entity.Version.HasValue
            ? ExpectedStreamState<TStreamPos>.AtVersion(entity.Version.Value)
            : ExpectedStreamState<TStreamPos>.DoesNotExist;

        await eventStore
            .AppendAsync(streamName, uncommittedEvents, options, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Versioned<TEntity>> LoadInternalAsync<TEntity>(
        string entityId,
        StreamNotFoundBehavior streamNotFoundBehavior,
        CancellationToken cancellationToken)
        where TEntity : notnull
    {
        var entityDefinition = GetEntityDefinition<TEntity>();
        var initialState = entityDefinition.CreateInitialState(); // May come from a snapshot (in future).

        // Used in closure of EnumerateEvents, so must be declared before the async enumerable is materialised, i.e.
        // before RestoreAsync is invoked.
        StreamPosition? loadedVersion = null;

        var entity = await entityDefinition
            .RestoreAsync(initialState, EnumerateEvents(), cancellationToken)
            .ConfigureAwait(false);

        return Versioned.From(entity, loadedVersion);

        async IAsyncEnumerable<TEvent> EnumerateEvents()
        {
            var streamName = GetStreamName<TEntity>(entityId);
            var readOptions = new ReadStreamOptions { StreamNotFoundBehavior = streamNotFoundBehavior };

            await foreach (var e in eventStore
                               .ReadStream(streamName, readOptions)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                loadedVersion = e.Context.StreamVersion;
                yield return e.Payload;
            }
        }
    }

    private IEntityDefinition<TEvent, TEntity> GetEntityDefinition<TEntity>() where TEntity : notnull
    {
        return entityDefinitionProvider.GetDefinition<TEntity>() ?? throw new ArgumentException(
            $"Entity definition not found for type '{typeof(TEntity)}'.",
            nameof(TEntity));
    }

    private static string GetStreamName<TEntity>(string entityId)
    {
        var streamNamePrefix = DefaultStreamNamePrefix.CreateFor(typeof(TEntity));
        return $"{streamNamePrefix}-{entityId}";
    }
}
*/