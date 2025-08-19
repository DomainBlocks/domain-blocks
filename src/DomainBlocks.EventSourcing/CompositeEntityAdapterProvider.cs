using System.Collections.Concurrent;

namespace DomainBlocks.EventSourcing;

public class CompositeEntityAdapterProvider(IEnumerable<IEntityAdapterProvider> providers) : IEntityAdapterProvider
{
    private readonly ConcurrentDictionary<Type, IEntityAdapter> _adapters = new();
    private readonly IEntityAdapterProvider[] _providers = providers.ToArray();

    public IEntityAdapter<TEntity>? GetAdapter<TEntity>() where TEntity : notnull
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var adapter))
            return (IEntityAdapter<TEntity>)adapter;

        var newAdapter = _providers.Select(x => x.GetAdapter<TEntity>()).FirstOrDefault(x => x != null);

        if (newAdapter != null)
            _adapters.TryAdd(typeof(TEntity), newAdapter);

        return newAdapter;
    }
}