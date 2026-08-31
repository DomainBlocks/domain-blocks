namespace DomainBlocks.EventSourcing;

public sealed class GenericStateAdapterResolver<TEvent, TStreamId> :
    IEventSourcedStateAdapterResolver<TEvent, TStreamId>
    where TEvent : notnull
    where TStreamId : notnull
{
    private readonly Type _genericTypeDefinition;
    private readonly Type _stateGenericArgType;
    private readonly object?[] _constructorArgs;

    public GenericStateAdapterResolver(Type genericTypeDefinition, params object?[] constructorArgs)
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);

        if (!genericTypeDefinition.IsClass || genericTypeDefinition.IsAbstract)
            throw new ArgumentException("Expected a concrete class type.", nameof(genericTypeDefinition));

        if (!genericTypeDefinition.IsGenericTypeDefinition)
            throw new ArgumentException("Expected a generic type definition.", nameof(genericTypeDefinition));

        var interfaceType = genericTypeDefinition
            .GetInterfaces()
            .SingleOrDefault(x => x.IsGenericType &&
                                  x.GetGenericTypeDefinition() == typeof(IEventSourcedStateAdapter<,,>));

        if (interfaceType == null)
        {
            throw new ArgumentException(
                $"State adapter type must implement {typeof(IEventSourcedStateAdapter<,,>).GetPrettyName()}.",
                nameof(genericTypeDefinition));
        }

        _stateGenericArgType = interfaceType.GetGenericArguments()[0];
        _genericTypeDefinition = genericTypeDefinition;
        _constructorArgs = constructorArgs;
    }

    public IEventSourcedStateAdapter<TState, TEvent, TStreamId>? Resolve<TState>() where TState : notnull
    {
        if (!_stateGenericArgType.TryBindGenericParameters(typeof(TState), out var bindings))
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

        return (IEventSourcedStateAdapter<TState, TEvent, TStreamId>)
            Activator.CreateInstance(definitionType, _constructorArgs)!;
    }
}