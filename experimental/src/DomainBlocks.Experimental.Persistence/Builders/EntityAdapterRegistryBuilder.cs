using DomainBlocks.Experimental.Persistence.Entities;

namespace DomainBlocks.Experimental.Persistence.Builders;

public class EntityAdapterRegistryBuilder
{
    private readonly Dictionary<Type, IEntityAdapter> _entityAdapters = new();
    private readonly List<GenericEntityAdapterFactory> _genericEntityAdapterFactories = new();

    public EntityAdapterRegistryBuilder Add<TEntity>(IEntityAdapter<TEntity> entityAdapter)
    {
        _entityAdapters.Add(typeof(TEntity), entityAdapter);
        return this;
    }

    public EntityAdapterRegistryBuilder MakeGenericFactory(Type entityAdapterType, params object?[]? constructorArgs)
    {
        var typeResolver = new GenericEntityAdapterTypeResolver(entityAdapterType);
        var factory = new GenericEntityAdapterFactory(typeResolver, constructorArgs);
        _genericEntityAdapterFactories.Add(factory);
        return this;
    }

    public EntityAdapterRegistry Build() => new(_entityAdapters, _genericEntityAdapterFactories);
}