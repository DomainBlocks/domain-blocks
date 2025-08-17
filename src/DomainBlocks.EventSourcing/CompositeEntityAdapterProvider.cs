using System.Collections.Concurrent;

namespace DomainBlocks.EventSourcing;

public class CompositeEntityAdapterProvider(IEnumerable<IEntityAdapterProvider> providers) : IEntityAdapterProvider
{
    private readonly ConcurrentDictionary<Type, IEntityAdapter> _adapters = new();
    private readonly IEntityAdapterProvider[] _providers = providers.ToArray();

    public IEntityAdapter<TEntity>? GetFor<TEntity>() where TEntity : notnull
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var result))
            return (IEntityAdapter<TEntity>)result;

        var adapter = _providers
            .Select(x => x.GetFor<TEntity>())
            .Where(x => x != null)
            .Select(x => x)
            .FirstOrDefault();

        if (adapter != null)
            _adapters.TryAdd(adapter.EntityType, adapter);

        return adapter;
    }
}