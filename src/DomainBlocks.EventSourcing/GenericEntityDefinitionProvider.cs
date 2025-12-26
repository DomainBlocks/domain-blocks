using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventSourcing;

public class GenericEntityDefinitionProvider<TEventBase> : IEntityDefinitionProvider<TEventBase>
    where TEventBase : class
{
    private readonly Type _genericTypeDefinition;
    private readonly Type _entityGenericArgType;
    private readonly object?[]? _constructorArgs;

    public GenericEntityDefinitionProvider(Type genericTypeDefinition, object?[]? constructorArgs = null)
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);

        if (!genericTypeDefinition.IsClass || genericTypeDefinition.IsAbstract)
            throw new ArgumentException("Expected a concrete class type.", nameof(genericTypeDefinition));

        if (!genericTypeDefinition.IsGenericTypeDefinition)
            throw new ArgumentException("Expected a generic type definition.", nameof(genericTypeDefinition));

        var entityDefinitionInterfaceType = genericTypeDefinition
            .GetInterfaces()
            .SingleOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEntityDefinition<,>));

        if (entityDefinitionInterfaceType == null)
        {
            throw new ArgumentException(
                $"Entity definition type must implement {typeof(IEntityDefinition<,>).GetPrettyName()}.",
                nameof(genericTypeDefinition));
        }

        // Check all generic parameters can be resolved via TEntity.
        var entityGenericArg = entityDefinitionInterfaceType.GetGenericArguments()[1];
        var reachableEntityParams = entityGenericArg.GetReachableGenericParameters();
        var definitionParams = genericTypeDefinition.GetGenericArguments().Where(x => x.IsGenericParameter).ToArray();
        var missingParams = definitionParams.Where(x => !reachableEntityParams.Contains(x)).ToArray();

        if (missingParams.Length > 0)
        {
            throw new ArgumentException(
                $"Invalid entity definition type '{genericTypeDefinition.GetPrettyName()}'. " +
                $"The following generic parameters are not reachable from '{entityGenericArg.GetPrettyName()}': " +
                $"{string.Join<Type>(", ", missingParams)}",
                nameof(genericTypeDefinition));
        }

        _genericTypeDefinition = genericTypeDefinition;
        _entityGenericArgType = entityGenericArg;
        _constructorArgs = constructorArgs;
    }

    public IEntityDefinition<TEventBase, TEntity>? GetDefinition<TEntity>() where TEntity : notnull
    {
        if (!TryResolveDefinitionType(typeof(TEntity), out var definitionType))
            return null;

        var definition = Activator.CreateInstance(definitionType, _constructorArgs)!;

        return (IEntityDefinition<TEventBase, TEntity>)definition;
    }

    private bool TryResolveDefinitionType(Type entityType, [NotNullWhen(true)] out Type? definitionType)
    {
        if (!_entityGenericArgType.TryBindGenericParameters(entityType, out var bindings))
        {
            definitionType = null;
            return false;
        }

        var definitionGenericParams = _genericTypeDefinition
            .GetGenericArguments()
            .Where(x => x.IsGenericParameter)
            .ToArray();

        if (!definitionGenericParams.All(bindings.ContainsKey))
        {
            definitionType = null;
            return false;
        }

        var genericArgs = new Type[definitionGenericParams.Length];

        foreach (var param in definitionGenericParams)
            genericArgs[param.GenericParameterPosition] = bindings[param];

        definitionType = _genericTypeDefinition.MakeGenericType(genericArgs);
        return true;
    }
}