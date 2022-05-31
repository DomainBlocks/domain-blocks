using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Aggregates;

public static class AggregateEventRouter
{
    public static AggregateEventRouter<TEventBase> Create<TEventBase>(EventRoutes<TEventBase> eventRoutes) =>
        new(eventRoutes);
}

/// <summary>
/// Routes domain events onto an aggregate and returns the updated aggregate state
/// </summary>
public sealed class AggregateEventRouter<TEventBase> : IAggregateEventRouter<TEventBase>
{
    private readonly EventRoutes<TEventBase> _routes;

    public AggregateEventRouter(EventRoutes<TEventBase> eventRoutes)
    {
        _routes = eventRoutes ?? throw new ArgumentNullException(nameof(eventRoutes));
    }

    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events)
    {
        return events.Aggregate(aggregateRoot, Send);
    }

    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events)
    {
        return events.Aggregate(aggregateRoot, Send);
    }

    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, TEventBase @event)
    {
        var eventApplier = _routes.Get<TAggregate>(@event.GetType());
        return eventApplier(aggregateRoot, @event);
    }
}