using System.Reflection;

namespace DomainBlocks.Persistence.Builders;

public class EventBaseTypeMappingBuilder<TEventBase> : IEventTypeMappingBuilder
{
    private IEnumerable<Assembly>? _searchAssemblies;
    private Func<Type, bool>? _typeFilter;

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
            from type in assembly.GetTypes()
            where type is { IsAbstract: false, IsInterface: false } &&
                  typeof(TEventBase).IsAssignableFrom(type) &&
                  (_typeFilter?.Invoke(type) ?? true)
            select new EventTypeMapping(type, type.Name);

        return eventTypeMappings;
    }
}