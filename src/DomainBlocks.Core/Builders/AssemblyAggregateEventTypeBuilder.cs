﻿using System.Reflection;

namespace DomainBlocks.Core.Builders;

internal sealed class AssemblyAggregateEventTypeBuilder<TAggregate, TEventBase> :
    IAutoAggregateEventTypeBuilder<TAggregate>,
    ITypeFilterBuilder
{
    private readonly Assembly _assembly;
    private Func<Type, bool>? _eventTypePredicate;

    public AssemblyAggregateEventTypeBuilder(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public void Where(Func<Type, bool> predicate)
    {
        _eventTypePredicate = predicate;
    }

    public IEnumerable<AggregateEventType<TAggregate>> Build()
    {
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

        if (_eventTypePredicate != null)
        {
            eventClrTypes = eventClrTypes.Where(_eventTypePredicate).ToList();

            if (eventClrTypes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Events with base type '{typeof(TEventBase).Name}' where found in assembly " +
                    $"'{_assembly.GetName().Name}', but none matched the specified predicate. ");
            }
        }

        return eventClrTypes.Select(x => new AggregateEventType<TAggregate>(x));
    }
}