using System.Reflection;
using DomainBlocks.Persistence.Events;

namespace DomainBlocks.Persistence.Builders;

public class EventBaseTypeMappingBuilder<TEventBase> : IEventTypeMappingBuilder
{
    private IEnumerable<Assembly>? _searchAssemblies;
    private Func<Type, bool>? _typeFilter;

    EventTypeMappingBuilderKind IEventTypeMappingBuilder.Kind => EventTypeMappingBuilderKind.EventBaseType;

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

    IEnumerable<EventTypeMapping> IEventTypeMappingBuilder.Build()
    {
        var eventTypeMappings =
            from assembly in _searchAssemblies ?? AppDomain.CurrentDomain.GetAssemblies()
            from t in assembly.GetTypes()
            where t is { IsAbstract: false, IsInterface: false } &&
                  t.IsAssignableTo(typeof(TEventBase)) &&
                  (_typeFilter?.Invoke(t) ?? true)
            select new EventTypeMapping(t, t.Name);

        return eventTypeMappings;
    }
}