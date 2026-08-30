namespace DomainBlocks.EventSourcing;

public sealed class GenericEventSourcedStateDefinitionProvider<TEvent, TStreamId>(
    Type genericTypeDefinition,
    params object?[] constructorArgs) :
    IEventSourcedStateDefinitionProvider<TEvent, TStreamId>
    where TEvent : notnull
    where TStreamId : notnull
{
    private readonly Type _genericTypeDefinition = Validate(genericTypeDefinition);

    public IEventSourcedStateDefinition<TState, TEvent, TStreamId>? GetDefinition<TState>() where TState : notnull
    {
        var interfaceType = _genericTypeDefinition
            .GetInterfaces()
            .Single(x => x.IsGenericType &&
                         x.GetGenericTypeDefinition() == typeof(IEventSourcedStateDefinition<,,>));

        var stateType = interfaceType.GetGenericArguments()[0];

        if (!stateType.TryBindGenericParameters(typeof(TState), out var bindings))
            return null;

        var definitionGenericParams = _genericTypeDefinition
            .GetGenericArguments()
            .Where(x => x.IsGenericParameter)
            .ToArray();

        if (!definitionGenericParams.All(bindings.ContainsKey))
            return null;

        var genericArgs = new Type[definitionGenericParams.Length];

        foreach (var param in definitionGenericParams)
            genericArgs[param.GenericParameterPosition] = bindings[param];

        var definitionType = _genericTypeDefinition.MakeGenericType(genericArgs);

        return (IEventSourcedStateDefinition<TState, TEvent, TStreamId>)
            Activator.CreateInstance(definitionType, constructorArgs)!;
    }

    private static Type Validate(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsClass || type.IsAbstract)
            throw new ArgumentException("Expected a concrete class type.", nameof(type));

        if (!type.IsGenericTypeDefinition)
            throw new ArgumentException("Expected a generic type definition.", nameof(type));

        return type;
    }
}