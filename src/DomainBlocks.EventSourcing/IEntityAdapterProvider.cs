namespace DomainBlocks.EventSourcing;

public interface IEntityAdapterProvider<TEventBase> where TEventBase : class
{
    IEntityAdapter<TEntity, TEventBase>? GetAdapter<TEntity>() where TEntity : notnull;
}