using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates;

public sealed class EventRoutes<TEventBase>
{
    private readonly Dictionary<(Type, Type), EventApplier<object, TEventBase>> _routes = new();

    public void Add<TAggregate, TEvent>(EventApplier<TAggregate, TEvent> eventApplier) where TEvent : TEventBase
    {
        _routes.Add((typeof(TAggregate), typeof(TEvent)), (agg, e) => eventApplier((TAggregate)agg, (TEvent)e));
    }

    public EventApplier<TAggregate, TEventBase> Get<TAggregate>(Type eventType)
    {
        if (!typeof(TEventBase).IsAssignableFrom(eventType))
        {
            throw new ArgumentException();
        }

        var key = (typeof(TAggregate), eventType);
        if (_routes.TryGetValue(key, out var eventApplier))
        {
            return (agg, e) => (TAggregate)eventApplier(agg, e);
        }

        // If we get here, there is no explicit route specified for this event type.
        // Try and get a route to the event base type, i.e. a default route.
        var defaultKey = (typeof(TAggregate), typeof(TEventBase));
        if (_routes.TryGetValue(defaultKey, out var defaultEventApplier))
        {
            return (agg, e) => (TAggregate)defaultEventApplier(agg, e);
        }

        // No default route specified.
        var message = "No route or default route found when attempting to apply event " +
                      $"{eventType.Name} to {typeof(TAggregate).Name}";
        throw new KeyNotFoundException(message);
    }
}