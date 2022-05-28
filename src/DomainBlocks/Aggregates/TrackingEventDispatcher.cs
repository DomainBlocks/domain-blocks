using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Aggregates;

public class TrackingEventDispatcher<TEventBase> : IEventDispatcher<TEventBase>
{
    private readonly EventDispatcher<TEventBase> _inner;
    private readonly List<TEventBase> _trackedEvents = new();

    public TrackingEventDispatcher(EventDispatcher<TEventBase> inner)
    {
        _inner = inner;
    }
    
    public IReadOnlyList<TEventBase> TrackedEvents => _trackedEvents.ToList().AsReadOnly();

    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events)
    {
        return _inner.Dispatch(aggregateRoot, events);
    }

    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events)
    {
        return _inner.Dispatch(aggregateRoot, events);
    }

    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, TEventBase @event)
    {
        var result = _inner.Dispatch(aggregateRoot, @event);
        _trackedEvents.Add(@event);
        return result;
    }
    
    public void ClearTrackedEvents() => _trackedEvents.Clear();
}