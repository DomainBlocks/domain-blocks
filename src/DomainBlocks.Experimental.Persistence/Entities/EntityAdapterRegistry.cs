using System.Collections.Concurrent;
using DomainBlocks.Experimental.Persistence.Extensions;

namespace DomainBlocks.Experimental.Persistence.Entities;

public class EntityAdapterRegistry
{
    private readonly ConcurrentDictionary<Type, IEntityAdapter> _adapters;
    private readonly IReadOnlyCollection<GenericEntityAdapterFactory> _genericAdapterFactories;

    public EntityAdapterRegistry(
        IReadOnlyDictionary<Type, IEntityAdapter> entityAdapters,
        IEnumerable<GenericEntityAdapterFactory> genericEntityAdapterFactories)
    {
        if (!entityAdapters.Values.All(x => x.GetType().HasInterface(typeof(IEntityAdapter<>))))
        {
            throw new ArgumentException(
                $"Entity adapters must not implement '{typeof(IEntityAdapter)}' directly.", nameof(entityAdapters));
        }

        _adapters = new ConcurrentDictionary<Type, IEntityAdapter>(entityAdapters);
        _genericAdapterFactories = genericEntityAdapterFactories.ToArray();
    }

    public bool TryGetFor<TEntity>(out IEntityAdapter<TEntity>? adapter)
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var result))
        {
            adapter = (IEntityAdapter<TEntity>)result;
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
            _adapters.TryAdd(adapter.EntityType, adapter);
        }

        return adapter != null;
    }
}