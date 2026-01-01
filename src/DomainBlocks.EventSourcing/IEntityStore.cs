namespace DomainBlocks.EventSourcing;

public interface IEntityStore
{
    /// <summary>
    /// Loads an entity from an event stream in the event store. If the stream does not exist, a
    /// <see cref="DomainBlocks.EventStore.Abstractions.Exceptions.StreamNotFoundException"/> is thrown.
    /// </summary>
    /// <param name="entityId">The ID of the entity</param>
    /// <param name="cancellationToken">A cancellation token</param>
    /// <typeparam name="TEntity">The type of the entity</typeparam>
    /// <returns>A typed entity instance</returns>
    Task<Versioned<TEntity>> LoadAsync<TEntity>(string entityId, CancellationToken cancellationToken = default)
        where TEntity : notnull;

    /// <summary>
    /// Creates or loads an entity from an event stream in the event store. If the stream does not exist, a new entity
    /// is created.
    /// </summary>
    /// <param name="entityId">The ID of the entity</param>
    /// <param name="cancellationToken">A cancellation token</param>
    /// <typeparam name="TEntity">The type of the entity</typeparam>
    /// <returns>A typed entity instance</returns>
    Task<Versioned<TEntity>> LoadOrCreateAsync<TEntity>(
        string entityId,
        CancellationToken cancellationToken = default)
        where TEntity : notnull;

    Task SaveAsync<TEntity>(Versioned<TEntity> entity, CancellationToken cancellationToken = default)
        where TEntity : notnull;
}