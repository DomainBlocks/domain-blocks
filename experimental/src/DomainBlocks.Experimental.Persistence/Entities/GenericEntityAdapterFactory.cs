namespace DomainBlocks.Experimental.Persistence.Entities;

public class GenericEntityAdapterFactory
{
    private readonly GenericEntityAdapterTypeResolver _typeResolver;
    private readonly object?[]? _constructorArgs;

    public GenericEntityAdapterFactory(Type entityAdapterType, object?[]? constructorArgs = null)
    {
        _typeResolver = new GenericEntityAdapterTypeResolver(entityAdapterType);
        _constructorArgs = constructorArgs;
    }

    public bool TryCreateFor<TEntity>(out IEntityAdapter<TEntity>? entityAdapter)
    {
        if (!_typeResolver.TryResolveFor<TEntity>(out var resolvedAdapterType))
        {
            entityAdapter = null;
            return false;
        }

        var instance = Activator.CreateInstance(resolvedAdapterType!, _constructorArgs);
        entityAdapter = (IEntityAdapter<TEntity>)instance!;
        return true;
    }
}