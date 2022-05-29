using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Aggregates;

public class TrackingAggregateEventRouter<TEventBase> : IAggregateEventRouter<TEventBase>
{
    private readonly IAggregateEventRouter<TEventBase> _inner;
    private readonly List<TEventBase> _trackedEvents = new();

    public TrackingAggregateEventRouter(IAggregateEventRouter<TEventBase> inner)
    {
        _inner = inner;
    }
    
    public IReadOnlyList<TEventBase> TrackedEvents => _trackedEvents.ToList().AsReadOnly();

    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events)
    {
        return _inner.Send(aggregateRoot, events);
    }

    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events)
    {
        return _inner.Send(aggregateRoot, events);
    }

    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, TEventBase @event)
    {
        var result = _inner.Send(aggregateRoot, @event);
        _trackedEvents.Add(@event);
        return result;
    }
    
    public void ClearTrackedEvents() => _trackedEvents.Clear();
}