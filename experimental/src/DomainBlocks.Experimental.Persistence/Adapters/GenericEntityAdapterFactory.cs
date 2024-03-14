using DomainBlocks.Experimental.Persistence.Extensions;

namespace DomainBlocks.Experimental.Persistence.Adapters;

public class GenericEntityAdapterFactory
{
    private readonly GenericEntityAdapterTypeResolver _typeResolver;
    private readonly object?[]? _constructorArgs;

    public GenericEntityAdapterFactory(Type entityAdapterType, object?[]? constructorArgs = null)
    {
        _typeResolver = new GenericEntityAdapterTypeResolver(entityAdapterType);
        _constructorArgs = constructorArgs;
    }

    public bool TryCreateFor<TEntity>(out EntityAdapter<TEntity>? entityAdapter)
    {
        if (!_typeResolver.TryResolveFor<TEntity>(out var resolvedAdapterType))
        {
            entityAdapter = null;
            return false;
        }

        var instance = Activator.CreateInstance(resolvedAdapterType!, _constructorArgs);

        var resolvedInterfaceType = resolvedAdapterType!
            .GetInterfaces()
            .Single(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEntityAdapter<,>));

        var resolvedStateType = resolvedInterfaceType.GetGenericArguments()[1];

        var createMethod = typeof(EntityAdapterExtensions)
            .GetMethod(nameof(EntityAdapterExtensions.HideStateType))!
            .MakeGenericMethod(typeof(TEntity), resolvedStateType);

        entityAdapter = (EntityAdapter<TEntity>)createMethod.Invoke(null, new[] { instance })!;
        return true;
    }
}