namespace DomainBlocks.EventSourcing;

public interface IEntityAdapterProvider<TEventBase> where TEventBase : class
{
    IEntityAdapter<TEventBase, TEntity>? GetAdapter<TEntity>() where TEntity : notnull;
}