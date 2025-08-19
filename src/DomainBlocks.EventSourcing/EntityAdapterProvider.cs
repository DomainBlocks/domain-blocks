using System.Collections.Frozen;

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

    public IEntityAdapter<TEntity>? GetAdapter<TEntity>() where TEntity : notnull
    {
        return (IEntityAdapter<TEntity>?)_adapters.GetValueOrDefault(typeof(TEntity));
    }
}