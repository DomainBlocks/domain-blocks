using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventSourcing;

public class CompositeEntityAdapterProvider(IEnumerable<IEntityAdapterProvider> providers) : IEntityAdapterProvider
{
    private readonly ConcurrentDictionary<Type, IEntityAdapter> _adapters = new();
    private readonly IEntityAdapterProvider[] _providers = providers.ToArray();

    public bool TryGetFor<TEntity>([NotNullWhen(true)] out IEntityAdapter<TEntity>? adapter) where TEntity : notnull
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var result))
        {
            adapter = (IEntityAdapter<TEntity>)result;
            return true;
        }

        adapter = _providers
            .Select(x => x.TryGetFor<TEntity>(out var instance) ? instance : null)
            .Where(x => x != null)
            .Select(x => x)
            .FirstOrDefault();

        if (adapter != null)
            _adapters.TryAdd(adapter.EntityType, adapter);

        return adapter != null;
    }
}