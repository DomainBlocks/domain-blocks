using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Aggregates;

/// <summary>
/// Dispatches domain events onto an aggregate and returns an updated aggregate state
/// </summary>
public sealed class EventDispatcher<TEventBase> : IEventDispatcher<TEventBase>
{
    private readonly EventRoutes<TEventBase> _routes;

    public EventDispatcher(EventRoutes<TEventBase> eventRoutes)
    {
        _routes = eventRoutes ?? throw new ArgumentNullException(nameof(eventRoutes));
    }
        
    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events)
    {
        return events.Aggregate(aggregateRoot, Dispatch);
    }

    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events)
    {
        return events.Aggregate(aggregateRoot, Dispatch);
    }

    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, TEventBase @event)
    {
        var eventApplier = _routes.Get<TAggregate>(@event.GetType());
        return eventApplier(aggregateRoot, @event);
    }
}