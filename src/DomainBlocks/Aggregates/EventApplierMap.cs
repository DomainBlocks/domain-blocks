using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates;

public class EventApplierMap<TEventBase>
{
    private readonly Dictionary<Type, Func<object, TEventBase, object>> _eventAppliers = new();

    public void Add<TAggregate>(Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _eventAppliers.Add(typeof(TAggregate), (state, @event) => eventApplier((TAggregate)state, @event));
    }

    public Func<TAggregate, TEventBase, TAggregate> Get<TAggregate>()
    {
        if (_eventAppliers.TryGetValue(typeof(TAggregate), out var eventApplier))
        {
            return (state, @event) => (TAggregate)eventApplier(state, @event);
        }
        
        // No event applier found.
        var message = $"No event applier found for type {typeof(TAggregate).FullName}";
        throw new KeyNotFoundException(message);
    }
}