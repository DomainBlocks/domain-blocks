using DomainBlocks.Experimental.Persistence.Extensions;

namespace DomainBlocks.Experimental.Persistence.Entities;

public class GenericEntityAdapterTypeResolver
{
    private readonly Type _adapterType;

    public GenericEntityAdapterTypeResolver(Type entityAdapterType)
    {
        if (entityAdapterType == null) throw new ArgumentNullException(nameof(entityAdapterType));

        if (!entityAdapterType.IsGenericTypeDefinition)
            throw new ArgumentException("Expected a generic type definition.", nameof(entityAdapterType));

        var interfaceType = entityAdapterType
            .GetInterfaces()
            .SingleOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEntityAdapter<,>));

        if (interfaceType == null)
            throw new ArgumentException(
                $"Entity adapter type must implement {typeof(IEntityAdapter<,>).GetPrettyName()}.",
                nameof(entityAdapterType));

        // TODO: Is the type instantiable?

        // Check all generic parameters can be resolved via TEntity.
        var entityGenericArg = interfaceType.GetGenericArguments()[0];
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

        _adapterType = entityAdapterType;
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

        var adapterGenericParams = _adapterType.GetGenericArguments().Where(x => x.IsGenericParameter).ToArray();
        if (!adapterGenericParams.All(x => resolvedGenericParams!.ContainsKey(x)))
        {
            return false;
        }

        var genericArgs = new Type[adapterGenericParams.Length];

        foreach (var param in adapterGenericParams)
        {
            genericArgs[param.GenericParameterPosition] = resolvedGenericParams![param];
        }

        resolvedType = _adapterType.MakeGenericType(genericArgs);
        return true;
    }
}