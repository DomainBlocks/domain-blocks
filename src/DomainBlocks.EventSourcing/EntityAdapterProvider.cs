using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventSourcing;

public class EntityAdapterProvider : IEntityAdapterProvider
{
    private readonly FrozenDictionary<Type, IEntityAdapter> _adapters;

    public EntityAdapterProvider(IEnumerable<IEntityAdapter> entityAdapters)
    {
        var adapters = entityAdapters.ToFrozenDictionary(x => x.EntityType);

        if (!adapters.Values.All(x => x.GetType().HasInterface(typeof(IEntityAdapter<>))))
        {
            throw new ArgumentException(
                $"Entity adapters must not implement '{typeof(IEntityAdapter)}' directly.", nameof(entityAdapters));
        }

        _adapters = adapters;
    }

    public bool TryGetFor<TEntity>([NotNullWhen(true)] out IEntityAdapter<TEntity>? adapter) where TEntity : notnull
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var result))
        {
            adapter = (IEntityAdapter<TEntity>)result;
            return true;
        }

        adapter = null;
        return false;
    }
}