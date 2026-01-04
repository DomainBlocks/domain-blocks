namespace DomainBlocks.EventSourcing;

public interface IEntityDefinitionProvider<TEvent> where TEvent : notnull
{
    IEntityDefinition<TEvent, TEntity>? GetDefinition<TEntity>() where TEntity : notnull;
}