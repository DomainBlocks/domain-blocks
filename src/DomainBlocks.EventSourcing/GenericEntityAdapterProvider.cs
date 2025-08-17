using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventSourcing;

public class GenericEntityAdapterProvider : IEntityAdapterProvider
{
    private readonly ConcurrentDictionary<Type, IEntityAdapter> _adapters = new();
    private readonly Type _genericTypeDefinition;
    private readonly Type _entityGenericArgType;
    private readonly object?[]? _constructorArgs;

    public GenericEntityAdapterProvider(Type genericTypeDefinition, object?[]? constructorArgs = null)
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);

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
        _constructorArgs = constructorArgs;
    }

    public IEntityAdapter<TEntity>? GetFor<TEntity>() where TEntity : notnull
    {
        if (_adapters.TryGetValue(typeof(TEntity), out var adapter))
            return (IEntityAdapter<TEntity>)adapter;

        if (!TryResolveAdapterType(typeof(TEntity), out var adapterType))
            return null;

        var newAdapter = (IEntityAdapter<TEntity>)Activator.CreateInstance(adapterType, _constructorArgs)!;
        _adapters.TryAdd(typeof(TEntity), newAdapter);

        return newAdapter;
    }

    private bool TryResolveAdapterType(Type entityType, [NotNullWhen(true)] out Type? adapterType)
    {
        if (!_entityGenericArgType.TryResolveGenericParametersFrom(entityType, out var resolvedGenericParams))
        {
            adapterType = null;
            return false;
        }

        var adapterGenericParams = _genericTypeDefinition
            .GetGenericArguments()
            .Where(x => x.IsGenericParameter)
            .ToArray();

        if (!adapterGenericParams.All(x => resolvedGenericParams.ContainsKey(x)))
        {
            adapterType = null;
            return false;
        }

        var genericArgs = new Type[adapterGenericParams.Length];

        foreach (var param in adapterGenericParams)
            genericArgs[param.GenericParameterPosition] = resolvedGenericParams[param];

        adapterType = _genericTypeDefinition.MakeGenericType(genericArgs);
        return true;
    }
}