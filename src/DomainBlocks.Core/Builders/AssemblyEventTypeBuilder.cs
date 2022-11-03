using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public class AssemblyEventTypeBuilder<TEventBase>
{
    private readonly Assembly _assembly;
    private Type _filterBaseType;

    public AssemblyEventTypeBuilder(Assembly assembly)
    {
        _assembly = assembly;
    }

    public void FilterByBaseType<TFilterBaseType>()
    {
        _filterBaseType = typeof(TFilterBaseType);
    }

    public IEnumerable<IEventType> Build()
    {
        if (_assembly == null) throw new ArgumentNullException(nameof(_assembly));

        var eventClrTypes = _assembly
            .GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && typeof(TEventBase).IsAssignableFrom(x))
            .ToList();

        if (eventClrTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"No events found in assembly '{_assembly.GetName().Name}' " +
                $"with base type '{typeof(TEventBase).Name}'.");
        }

        if (_filterBaseType != null)
        {
            eventClrTypes = eventClrTypes
                .Where(x => _filterBaseType.IsAssignableFrom(x))
                .ToList();

            if (eventClrTypes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No events found in assembly '{_assembly.GetName().Name}' " +
                    $"with filter base type '{_filterBaseType.Name}'.");
            }
        }

        var eventTypes = eventClrTypes
            .Select(eventClrType =>
            {
                var typeArgs = new[] { eventClrType, typeof(TEventBase) };
                var eventTypeClrType = typeof(EventType<,>).MakeGenericType(typeArgs);
                var eventType = (IEventType)Activator.CreateInstance(eventTypeClrType);
                return eventType;
            });

        return eventTypes;
    }
}