namespace DomainBlocks.EventSourcing;

public interface IEntityAdapterProvider
{
    IEntityAdapter<TEntity>? GetAdapter<TEntity>() where TEntity : notnull;
}