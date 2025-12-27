using System.Collections.Concurrent;

namespace DomainBlocks.EventSourcing;

public class CompositeEntityDefinitionProvider<TEventBase>(
    IEnumerable<IEntityDefinitionProvider<TEventBase>> providers) :
    IEntityDefinitionProvider<TEventBase>
    where TEventBase : class
{
    private readonly ConcurrentDictionary<Type, IEntityDefinition<TEventBase>> _definitions = new();
    private readonly IEntityDefinitionProvider<TEventBase>[] _providers = providers.ToArray();

    public IEntityDefinition<TEventBase, TEntity>? GetDefinition<TEntity>() where TEntity : notnull
    {
        if (_definitions.TryGetValue(typeof(TEntity), out var definition))
            return (IEntityDefinition<TEventBase, TEntity>)definition;

        var newDefinition = _providers.Select(x => x.GetDefinition<TEntity>()).FirstOrDefault(x => x != null);

        if (newDefinition != null)
            _definitions.TryAdd(typeof(TEntity), newDefinition);

        return newDefinition;
    }
}