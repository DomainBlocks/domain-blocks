using System.Collections.Frozen;

namespace DomainBlocks.EventSourcing;

public class EntityDefinitionProvider<TEventBase> : IEntityDefinitionProvider<TEventBase> where TEventBase : class
{
    private readonly FrozenDictionary<Type, IEntityDefinition<TEventBase>> _definitions;

    public EntityDefinitionProvider(IEnumerable<IEntityDefinition<TEventBase>> definitions)
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

    public IEntityDefinition<TEventBase, TEntity>? GetDefinition<TEntity>() where TEntity : notnull
    {
        return (IEntityDefinition<TEventBase, TEntity>?)_definitions.GetValueOrDefault(typeof(TEntity));
    }
}