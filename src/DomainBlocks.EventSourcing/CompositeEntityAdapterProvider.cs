using System.Collections.Concurrent;

namespace DomainBlocks.EventSourcing;

public class CompositeEntityAdapterProvider<TEventBase>(IEnumerable<IEntityAdapterProvider<TEventBase>> providers) :
    IEntityAdapterProvider<TEventBase>
    where TEventBase : class
{
    private readonly ConcurrentDictionary<Type, IEntityAdapter> _adapters = new();
    private readonly IEntityAdapterProvider<TEventBase>[] _providers = providers.ToArray();

    public IEntityAdapter<TEntity, TEventBase>? GetAdapter<TEntity>() where TEntity : notnull
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var adapter))
            return (IEntityAdapter<TEntity, TEventBase>)adapter;

        var newAdapter = _providers.Select(x => x.GetAdapter<TEntity>()).FirstOrDefault(x => x != null);

        if (newAdapter != null)
            _adapters.TryAdd(typeof(TEntity), newAdapter);

        return newAdapter;
    }
}