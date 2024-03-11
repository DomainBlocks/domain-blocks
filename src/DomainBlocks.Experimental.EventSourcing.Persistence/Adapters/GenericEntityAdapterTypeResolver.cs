using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;

public class GenericEntityAdapterTypeResolver
{
    private readonly Type _adapterType;
    private readonly Type _entityGenericArg;

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
        _entityGenericArg = entityGenericArg;
    }

    public bool TryResolveFor<TEntity>(out Type? resolvedType)
    {
        return TryResolveFor(typeof(TEntity), out resolvedType);
    }

    public bool TryResolveFor(Type entityType, out Type? resolvedType)
    {
        resolvedType = null;

        if (!_entityGenericArg.TryResolveGenericParametersFrom(entityType, out var resolvedGenericParams))
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