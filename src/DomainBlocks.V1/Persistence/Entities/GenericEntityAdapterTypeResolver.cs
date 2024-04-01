using System.Diagnostics;
using DomainBlocks.V1.Persistence.Extensions;

namespace DomainBlocks.V1.Persistence.Entities;

public class GenericEntityAdapterTypeResolver
{
    private readonly Type _genericTypeDefinition;
    private readonly Type _entityGenericArgType;

    public GenericEntityAdapterTypeResolver(Type genericTypeDefinition)
    {
        if (genericTypeDefinition == null) throw new ArgumentNullException(nameof(genericTypeDefinition));

        if (!genericTypeDefinition.IsClass || genericTypeDefinition.IsAbstract)
            throw new ArgumentException("Expected a concrete class type.", nameof(genericTypeDefinition));

        if (!genericTypeDefinition.IsGenericTypeDefinition)
            throw new ArgumentException("Expected a generic type definition.", nameof(genericTypeDefinition));

        var entityAdapterInterfaceType = genericTypeDefinition
            .GetInterfaces()
            .SingleOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEntityAdapter<>));

        if (entityAdapterInterfaceType == null)
            throw new ArgumentException(
                $"Entity adapter type must implement {typeof(IEntityAdapter<>).GetPrettyName()}.",
                nameof(genericTypeDefinition));

        // Check all generic parameters can be resolved via TEntity.
        var entityGenericArg = entityAdapterInterfaceType.GetGenericArguments()[0];
        var reachableEntityParams = entityGenericArg.FindReachableGenericParameters();
        var adapterParams = genericTypeDefinition.GetGenericArguments().Where(x => x.IsGenericParameter).ToArray();
        var unresolvedParams = adapterParams.Where(x => !reachableEntityParams.Contains(x)).ToArray();

        if (unresolvedParams.Length > 0)
        {
            throw new ArgumentException(
                $"Invalid entity adapter type '{genericTypeDefinition.GetPrettyName()}'. " +
                $"The following generic parameters are not reachable from '{entityGenericArg.GetPrettyName()}': " +
                $"{string.Join<Type>(", ", unresolvedParams)}",
                nameof(genericTypeDefinition));
        }

        _genericTypeDefinition = genericTypeDefinition;
        _entityGenericArgType = entityGenericArg;
    }

    public bool TryResolveFor<TEntity>(out Type? resolvedType) where TEntity : notnull
    {
        return TryResolveFor(typeof(TEntity), out resolvedType);
    }

    private bool TryResolveFor(Type entityType, out Type? resolvedType)
    {
        resolvedType = null;

        if (!_entityGenericArgType.TryResolveGenericParametersFrom(entityType, out var resolvedGenericParams))
        {
            return false;
        }

        Debug.Assert(resolvedGenericParams != null, nameof(resolvedGenericParams) + " != null");

        var adapterGenericParams = _genericTypeDefinition
            .GetGenericArguments()
            .Where(x => x.IsGenericParameter)
            .ToArray();

        if (!adapterGenericParams.All(x => resolvedGenericParams.ContainsKey(x)))
        {
            return false;
        }

        var genericArgs = new Type[adapterGenericParams.Length];

        foreach (var param in adapterGenericParams)
        {
            genericArgs[param.GenericParameterPosition] = resolvedGenericParams[param];
        }

        resolvedType = _genericTypeDefinition.MakeGenericType(genericArgs);
        return true;
    }
}