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
        // TODO (DS): Support more than one set of projections in a future PR.
        var projectionRegistry = _projectionOptions.First().ToProjectionRegistry();
        return _eventDispatcherFactory(projectionRegistry);
    }
}