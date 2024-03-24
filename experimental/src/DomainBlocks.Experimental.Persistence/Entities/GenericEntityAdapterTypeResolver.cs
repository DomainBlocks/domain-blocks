using DomainBlocks.Experimental.Persistence.Extensions;

namespace DomainBlocks.Experimental.Persistence.Entities;

public class GenericEntityAdapterTypeResolver
{
    private readonly Type _entityAdapterType;

    public GenericEntityAdapterTypeResolver(Type entityAdapterType)
    {
        if (entityAdapterType == null) throw new ArgumentNullException(nameof(entityAdapterType));

        if (!entityAdapterType.IsClass || entityAdapterType.IsAbstract)
            throw new ArgumentException("Expected a concrete class type.", nameof(entityAdapterType));

        if (!entityAdapterType.IsGenericTypeDefinition)
            throw new ArgumentException("Expected a generic type definition.", nameof(entityAdapterType));

        var entityAdapterInterfaceType = entityAdapterType
            .GetInterfaces()
            .SingleOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEntityAdapter<>));

        if (entityAdapterInterfaceType == null)
            throw new ArgumentException(
                $"Entity adapter type must implement {typeof(IEntityAdapter<>).GetPrettyName()}.",
                nameof(entityAdapterType));

        // Check all generic parameters can be resolved via TEntity.
        var entityGenericArg = entityAdapterInterfaceType.GetGenericArguments()[0];
        var reachableEntityParams = entityGenericArg.FindReachableGenericParameters();
        var adapterParams = entityAdapterType.GetGenericArguments().Where(x => x.IsGenericParameter).ToArray();
        var unresolvedParams = adapterParams.Where(x => !reachableEntityParams.Contains(x)).ToArray();

        if (unresolvedParams.Length > 0)
        {
            throw new ArgumentException(
                $"Invalid entity adapter type '{entityAdapterType.GetPrettyName()}'. " +
                $"The following generic parameters are not reachable from '{entityGenericArg.GetPrettyName()}': " +
                $"{string.Join<Type>(", ", unresolvedParams)}",
                nameof(entityAdapterType));
        }

        _entityAdapterType = entityAdapterType;
        EntityGenericArgType = entityGenericArg;
    }

    public Type EntityGenericArgType { get; }

    public bool TryResolveFor<TEntity>(out Type? resolvedType) => TryResolveFor(typeof(TEntity), out resolvedType);

    public bool TryResolveFor(Type entityType, out Type? resolvedType)
    {
        resolvedType = null;

        if (!EntityGenericArgType.TryResolveGenericParametersFrom(entityType, out var resolvedGenericParams))
        {
            return false;
        }

        var adapterGenericParams = _entityAdapterType.GetGenericArguments().Where(x => x.IsGenericParameter).ToArray();
        if (!adapterGenericParams.All(x => resolvedGenericParams!.ContainsKey(x)))
        {
            return false;
        }

        var genericArgs = new Type[adapterGenericParams.Length];

        foreach (var param in adapterGenericParams)
        {
            genericArgs[param.GenericParameterPosition] = resolvedGenericParams![param];
        }

        resolvedType = _entityAdapterType.MakeGenericType(genericArgs);
        return true;
    }
}