﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public interface IAssemblyEventOptionsBuilder
{
    /// <summary>
    /// Specify an additional filter to use on the event types which have been found in the specified assembly. The
    /// argument of the predicate is a type derived from the specified base event type.
    /// </summary>
    void Where(Func<Type, bool> predicate);
}

internal sealed class AssemblyEventOptionsBuilder<TAggregate, TEventBase> :
    IAssemblyEventOptionsBuilder,
    IAutoEventOptionsBuilder<TAggregate>
{
    private readonly Assembly _assembly;
    private Func<Type, bool> _eventTypePredicate;

    public AssemblyEventOptionsBuilder(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public void Where(Func<Type, bool> predicate)
    {
        _eventTypePredicate = predicate;
    }

    public IEnumerable<EventOptions<TAggregate>> Build()
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

        return eventClrTypes.Select(x => new EventOptions<TAggregate>(x));
    }
}