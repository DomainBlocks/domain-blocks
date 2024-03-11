using System.Reflection;
using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public sealed class EventBaseTypeMappingBuilder<TEventBase> : IEventTypeMappingBuilder
{
    private IEnumerable<Assembly>? _searchAssemblies;
    private Func<Type, bool>? _typeFilter;

    public EventTypeMappingBuilderKind Kind => EventTypeMappingBuilderKind.EventBaseType;

    public EventBaseTypeMappingBuilder<TEventBase> FromAssemblies(params Assembly[] searchAssemblies)
    {
        _searchAssemblies = searchAssemblies;
        return this;
    }

    public EventBaseTypeMappingBuilder<TEventBase> FromAssembly(Assembly assembly)
    {
        return FromAssemblies(assembly);
    }

    public EventBaseTypeMappingBuilder<TEventBase> FromAssemblyOf<T>()
    {
        return FromAssemblies(typeof(T).Assembly);
    }

    public EventBaseTypeMappingBuilder<TEventBase> Where(Func<Type, bool> typeFilter)
    {
        _typeFilter = typeFilter;
        return this;
    }

    public IEnumerable<EventTypeMapping> Build()
    {
        var eventTypeMappings = (_searchAssemblies ?? AppDomain.CurrentDomain.GetAssemblies())
            .FindConcreteTypesAssignableTo<TEventBase>()
            .Where(x => _typeFilter?.Invoke(x) ?? true)
            .Select(x => new EventTypeMapping(x, x.Name, null));

        return eventTypeMappings;
    }
}