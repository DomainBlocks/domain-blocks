namespace DomainBlocks.EventSourcing;

public interface IEntityAdapterProvider
{
    IEntityAdapter<TEntity>? GetFor<TEntity>() where TEntity : notnull;
}