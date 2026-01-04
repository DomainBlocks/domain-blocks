using System.Collections.Frozen;

namespace DomainBlocks.EventSourcing;

public class EntityDefinitionProvider<TEvent> : IEntityDefinitionProvider<TEvent> where TEvent : notnull
{
    private readonly FrozenDictionary<Type, IEntityDefinition<TEvent>> _definitions;

    public EntityDefinitionProvider(IEnumerable<IEntityDefinition<TEvent>> definitions)
    {
        var definitionDict = definitions.ToFrozenDictionary(x => x.EntityType);

        if (!definitionDict.Values.All(x => x.GetType().HasInterface(typeof(IEntityDefinition<,>))))
        {
            throw new ArgumentException(
                $"Entity definitions must not implement '{typeof(IEntityDefinition<>)}' directly.",
                nameof(definitions));
        }

        _definitions = definitionDict;
    }

    public IEntityDefinition<TEvent, TEntity>? GetDefinition<TEntity>() where TEntity : notnull
    {
        return (IEntityDefinition<TEvent, TEntity>?)_definitions.GetValueOrDefault(typeof(TEntity));
    }
}