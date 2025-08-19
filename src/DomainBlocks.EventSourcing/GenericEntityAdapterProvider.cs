using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventSourcing;

public class GenericEntityAdapterProvider : IEntityAdapterProvider
{
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
        var reachableEntityParams = entityGenericArg.GetReachableGenericParameters();
        var adapterParams = genericTypeDefinition.GetGenericArguments().Where(x => x.IsGenericParameter).ToArray();
        var missingParams = adapterParams.Where(x => !reachableEntityParams.Contains(x)).ToArray();

        if (missingParams.Length > 0)
        {
            throw new ArgumentException(
                $"Invalid entity adapter type '{genericTypeDefinition.GetPrettyName()}'. " +
                $"The following generic parameters are not reachable from '{entityGenericArg.GetPrettyName()}': " +
                $"{string.Join<Type>(", ", missingParams)}",
                nameof(genericTypeDefinition));
        }

        _genericTypeDefinition = genericTypeDefinition;
        _entityGenericArgType = entityGenericArg;
        _constructorArgs = constructorArgs;
    }

    public IEntityAdapter<TEntity>? GetAdapter<TEntity>() where TEntity : notnull
    {
        if (!TryResolveAdapterType(typeof(TEntity), out var adapterType))
            return null;

        var adapter = (IEntityAdapter<TEntity>)Activator.CreateInstance(adapterType, _constructorArgs)!;

        return adapter;
    }

    private bool TryResolveAdapterType(Type entityType, [NotNullWhen(true)] out Type? adapterType)
    {
        if (!_entityGenericArgType.TryBindGenericParameters(entityType, out var bindings))
        {
            adapterType = null;
            return false;
        }

        var adapterGenericParams = _genericTypeDefinition
            .GetGenericArguments()
            .Where(x => x.IsGenericParameter)
            .ToArray();

        if (!adapterGenericParams.All(x => bindings.ContainsKey(x)))
        {
            adapterType = null;
            return false;
        }

        var genericArgs = new Type[adapterGenericParams.Length];

        foreach (var param in adapterGenericParams)
            genericArgs[param.GenericParameterPosition] = bindings[param];

        adapterType = _genericTypeDefinition.MakeGenericType(genericArgs);
        return true;
    }
}