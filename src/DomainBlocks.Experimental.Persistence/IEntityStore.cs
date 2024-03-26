namespace DomainBlocks.Experimental.Persistence;

public interface IEntityStore
{
    Task<TEntity> LoadAsync<TEntity>(string entityId, CancellationToken cancellationToken = default);

    Task SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default);
}