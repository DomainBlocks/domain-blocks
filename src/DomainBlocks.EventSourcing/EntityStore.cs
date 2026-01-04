using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public sealed class EntityStore<TEvent>(
    IEventStoreClient<TEvent> eventStoreClient,
    IEntityDefinitionProvider<TEvent> entityDefinitionProvider) : IEntityStore where TEvent : notnull
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
            .Select(e => AppendEvent.Create(e, metadata))
            .ToArray();

        if (uncommittedEvents.Length == 0)
            return;

        var streamName = GetStreamName<TEntity>(entityId);

        var options = new AppendToStreamOptions
        {
            ExpectedState = entity.Version.HasValue
                ? ExpectedStreamState.SpecificVersion(entity.Version.Value)
                : ExpectedStreamState.StreamDoesNotExist
        };

        await eventStoreClient
            .AppendToStreamAsync(streamName, uncommittedEvents, options, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Versioned<TEntity>> LoadInternalAsync<TEntity>(
        string entityId,
        StreamNotFoundBehavior streamNotFoundBehavior,
        CancellationToken cancellationToken)
        where TEntity : notnull
    {
        var streamName = GetStreamName<TEntity>(entityId);
        var readOptions = new ReadStreamOptions { StreamNotFoundBehavior = streamNotFoundBehavior };
        var events = eventStoreClient.ReadStreamAsync(streamName, readOptions, cancellationToken);

        var entityDefinition = GetEntityDefinition<TEntity>();
        var initialState = entityDefinition.CreateInitialState(); // May come from a snapshot (in future).

        // Used in closure of EnumerateEvents, so must be declared before the async enumerable is materialised, i.e.
        // before RestoreAsync is invoked.
        StreamVersion? loadedVersion = null;

        var entity = await entityDefinition
            .RestoreAsync(initialState, EnumerateEvents(), cancellationToken)
            .ConfigureAwait(false);

        return Versioned.From(entity, loadedVersion);

        async IAsyncEnumerable<TEvent> EnumerateEvents()
        {
            await foreach (var e in events.ConfigureAwait(false))
            {
                loadedVersion = e.Context.StreamVersion;
                yield return e.Event;
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