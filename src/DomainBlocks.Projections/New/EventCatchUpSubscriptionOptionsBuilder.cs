using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptionsBuilder
{
    private readonly List<ProjectionOptionsBase> _allProjectionOptions = new();
    private Func<ProjectionRegistry, IEventDispatcher> _eventDispatcherFactory;

    public void AddProjection(Action<StatelessProjectionOptionsBuilder> optionsAction)
    {
        var projectionOptions = new StatelessProjectionOptions();
        var projectionOptionsBuilder = new StatelessProjectionOptionsBuilder(projectionOptions);
        _allProjectionOptions.Add(projectionOptions);
        optionsAction(projectionOptionsBuilder);
    }

    public ResourceProjectionOptionsBuilder<TResource> Using<TResource>(Func<TResource> resourceFactory)
        where TResource : IDisposable
    {
        var projectionOptions = new ResourceProjectionOptions<TResource>();
        projectionOptions.WithResourceFactory(resourceFactory);
        _allProjectionOptions.Add(projectionOptions);
        return new ResourceProjectionOptionsBuilder<TResource>(projectionOptions);
    }

    public void WithEventDispatcher(Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        _eventDispatcherFactory = eventDispatcherFactory;
    }

    public EventCatchUpSubscriptionOptions Build()
    {
        // TODO (DS): Support more than one set of projections.
        var projectionRegistry = _allProjectionOptions.First().ToProjectionRegistry();
        var eventDispatcher = _eventDispatcherFactory(projectionRegistry);
        return new EventCatchUpSubscriptionOptions(eventDispatcher);
    }
}