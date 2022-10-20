using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptions
{
    private readonly List<IProjectionOptions> _projectionOptions = new();
    private readonly List<IProjectionOptionsProvider> _projectionOptionsProviders = new();
    private Func<ProjectionRegistry, IEventDispatcher> _eventDispatcherFactory;

    public EventCatchUpSubscriptionOptions()
    {
    }

    private EventCatchUpSubscriptionOptions(EventCatchUpSubscriptionOptions copyFrom)
    {
        _projectionOptions = new List<IProjectionOptions>(copyFrom._projectionOptions);
        _projectionOptionsProviders = new List<IProjectionOptionsProvider>(copyFrom._projectionOptionsProviders);
        _eventDispatcherFactory = copyFrom._eventDispatcherFactory;
    }

    public EventCatchUpSubscriptionOptions WithProjectionOptions(IProjectionOptions projectionOptions)
    {
        var copy = new EventCatchUpSubscriptionOptions(this);
        copy._projectionOptions.Add(projectionOptions);
        return copy;
    }

    public EventCatchUpSubscriptionOptions WithProjectionOptionsProvider(
        IProjectionOptionsProvider projectionOptionsProvider)
    {
        var copy = new EventCatchUpSubscriptionOptions(this);
        copy._projectionOptionsProviders.Add(projectionOptionsProvider);
        return copy;
    }

    public EventCatchUpSubscriptionOptions WithEventDispatcherFactory(
        Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        return new EventCatchUpSubscriptionOptions(this) { _eventDispatcherFactory = eventDispatcherFactory };
    }

    public IEventDispatcher CreateEventDispatcher()
    {
        var allProjectionsOptions = _projectionOptionsProviders
            .SelectMany(x => x.GetProjectionOptions())
            .Concat(_projectionOptions);

        // TODO (DS): Support more than one set of projections.
        var projectionRegistry = allProjectionsOptions.First().ToProjectionRegistry();
        return _eventDispatcherFactory(projectionRegistry);
    }
}