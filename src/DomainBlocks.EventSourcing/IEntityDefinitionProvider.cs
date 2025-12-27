namespace DomainBlocks.EventSourcing;

public interface IEntityDefinitionProvider<TEventBase> where TEventBase : class
{
    IEntityDefinition<TEventBase, TEntity>? GetDefinition<TEntity>() where TEntity : notnull;
}