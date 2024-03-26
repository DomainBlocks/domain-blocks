using DomainBlocks.Experimental.Persistence.Entities;

namespace DomainBlocks.Experimental.Persistence.Builders;

public class EntityAdapterRegistryBuilder
{
    private readonly Dictionary<Type, IEntityAdapter> _adapters = new();
    private readonly List<GenericEntityAdapterFactoryBuilder> _factoryBuilders = new();

    public EntityAdapterRegistryBuilder Add<TEntity>(IEntityAdapter<TEntity> entityAdapter)
    {
        _adapters.Add(typeof(TEntity), entityAdapter);
        return this;
    }

    public GenericEntityAdapterFactoryBuilder AddGenericFactoryFor(Type genericTypeDefinition)
    {
        var builder = new GenericEntityAdapterFactoryBuilder(genericTypeDefinition);
        _factoryBuilders.Add(builder);
        return builder;
    }

    public EntityAdapterRegistry Build()
    {
        var factories = _factoryBuilders.Select(x => x.Build());
        return new EntityAdapterRegistry(_adapters, factories);
    }
}