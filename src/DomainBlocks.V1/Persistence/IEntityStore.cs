namespace DomainBlocks.V1.Persistence;

public interface IEntityStore
{
    Task<TEntity> LoadAsync<TEntity>(string entityId, CancellationToken cancellationToken = default)
        where TEntity : notnull;

    Task SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : notnull;
}