using System.Collections.Concurrent;

namespace DomainBlocks.EventSourcing;

public class CompositeEntityDefinitionProvider<TEvent>(
    IEnumerable<IEntityDefinitionProvider<TEvent>> providers) :
    IEntityDefinitionProvider<TEvent>
    where TEvent : notnull
{
    private readonly ConcurrentDictionary<Type, IEntityDefinition<TEvent>> _definitions = new();
    private readonly IEntityDefinitionProvider<TEvent>[] _providers = providers.ToArray();

    public IEntityDefinition<TEvent, TEntity>? GetDefinition<TEntity>() where TEntity : notnull
    {
        if (_definitions.TryGetValue(typeof(TEntity), out var definition))
            return (IEntityDefinition<TEvent, TEntity>)definition;

        var newDefinition = _providers.Select(x => x.GetDefinition<TEntity>()).FirstOrDefault(x => x != null);

        if (newDefinition != null)
            _definitions.TryAdd(typeof(TEntity), newDefinition);

        return newDefinition;
    }
}