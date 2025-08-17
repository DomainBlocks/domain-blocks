using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventSourcing;

public class GenericEntityAdapterFactory(
    GenericEntityAdapterTypeResolver typeResolver,
    object?[]? constructorArgs = null)
{
    public bool TryCreateFor<TEntity>([NotNullWhen(true)] out IEntityAdapter<TEntity>? entityAdapter)
        where TEntity : notnull
    {
        if (!typeResolver.TryResolveFor<TEntity>(out var resolvedAdapterType))
        {
            entityAdapter = null;
            return false;
        }

        var instance = Activator.CreateInstance(resolvedAdapterType, constructorArgs);
        entityAdapter = (IEntityAdapter<TEntity>)instance!;
        return true;
    }
}