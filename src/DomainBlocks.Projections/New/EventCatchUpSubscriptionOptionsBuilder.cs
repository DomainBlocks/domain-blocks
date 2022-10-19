using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptionsBuilder
{
    private readonly List<ProjectionOptions> _allProjectionOptions = new();
    private Func<ProjectionRegistry, IEventDispatcher> _eventDispatcherFactory;

    public void AddProjection(Action<ProjectionOptionsBuilder> optionsAction)
    {
        var projectionOptions = new ProjectionOptions();
        var projectionOptionsBuilder = new ProjectionOptionsBuilder(projectionOptions);
        _allProjectionOptions.Add(projectionOptions);
        optionsAction(projectionOptionsBuilder);
    }

    public ProjectionOptionsBuilder<TResource> Using<TResource>(Func<TResource> resourceFactory)
        where TResource : IDisposable
    {
        var projectionOptions = new ProjectionOptions<TResource>();
        projectionOptions.WithResourceFactory(resourceFactory);
        _allProjectionOptions.Add(projectionOptions);
        return new ProjectionOptionsBuilder<TResource>(projectionOptions);
    }

    public void WithEventDispatcher(Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        _eventDispatcherFactory = eventDispatcherFactory;
    }

    public EventCatchUpSubscriptionOptions Build()
    {
        // TODO (DS): Support more than one set of projections.
        //var projections = _projectionRegistryFactory();
        var projections = _allProjectionOptions.First().ProjectionRegistryFactory();
        var eventDispatcher = _eventDispatcherFactory(projections);
        return new EventCatchUpSubscriptionOptions(eventDispatcher);
    }
}