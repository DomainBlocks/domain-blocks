using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptions
{
    private readonly List<IProjectionOptions> _projectionOptions = new();
    private Func<ProjectionRegistry, IEventDispatcher> _eventDispatcherFactory;

    public EventCatchUpSubscriptionOptions()
    {
    }

    private EventCatchUpSubscriptionOptions(EventCatchUpSubscriptionOptions copyFrom)
    {
        _projectionOptions = new List<IProjectionOptions>(copyFrom._projectionOptions);
        _eventDispatcherFactory = copyFrom._eventDispatcherFactory;
    }

    public EventCatchUpSubscriptionOptions AddProjectionOptions(IProjectionOptions projectionOptions)
    {
        var copy = new EventCatchUpSubscriptionOptions(this);
        copy._projectionOptions.Add(projectionOptions);
        return copy;
    }

    public EventCatchUpSubscriptionOptions WithEventDispatcherFactory(
        Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        return new EventCatchUpSubscriptionOptions(this) { _eventDispatcherFactory = eventDispatcherFactory };
    }

    public IEventDispatcher CreateEventDispatcher()
    {
        var projectionRegistry = _projectionOptions
            .Aggregate(new ProjectionRegistry(), (acc, next) => next.Register(acc));

        return _eventDispatcherFactory(projectionRegistry);
    }
}