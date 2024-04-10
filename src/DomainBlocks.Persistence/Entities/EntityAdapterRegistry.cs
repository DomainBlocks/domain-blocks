using System.Collections.Concurrent;
using DomainBlocks.Persistence.Extensions;

namespace DomainBlocks.Persistence.Entities;

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

    public bool TryGetFor<TEntity>(out IEntityAdapter<TEntity>? adapter) where TEntity : notnull
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var result))
        {
            adapter = (IEntityAdapter<TEntity>)result;
            return true;
        }

        adapter = _genericAdapterFactories
            .Select(x =>
            {
                x.TryCreateFor<TEntity>(out var instance);
                return instance;
            })
            .Where(x => x != null)
            .Select(x => x)
            .FirstOrDefault();

        if (adapter != null)
        {
            _adapters.TryAdd(adapter.EntityType, adapter);
        }

        return adapter != null;
    }
}