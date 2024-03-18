using System.Collections.Concurrent;

namespace DomainBlocks.Experimental.Persistence.Entities;

public class EntityAdapterProvider
{
    private readonly ConcurrentDictionary<Type, IEntityAdapter> _adapters;
    private readonly IReadOnlyCollection<GenericEntityAdapterFactory> _genericAdapterFactories;

    public EntityAdapterProvider(
        IEnumerable<IEntityAdapter> entityAdapters,
        IEnumerable<GenericEntityAdapterFactory> genericEntityAdapterFactories)
    {
        _adapters = new ConcurrentDictionary<Type, IEntityAdapter>(entityAdapters.ToDictionary(x => x.EntityType));
        _genericAdapterFactories = genericEntityAdapterFactories.ToArray();
    }

    public bool TryGetFor<TEntity>(out EntityAdapter<TEntity>? adapter)
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var result))
        {
            adapter = (EntityAdapter<TEntity>)result;
            return true;
        }

        adapter = _genericAdapterFactories
            .Select(x =>
            {
                var success = x.TryCreateFor<TEntity>(out var instance);
                return (success, instance);
            })
            .Where(x => x.success)
            .Select(x => x.instance)
            .FirstOrDefault();

        if (adapter != null)
        {
            _adapters.TryAdd(adapter.StateType, adapter);
        }

        return adapter != null;
    }
}