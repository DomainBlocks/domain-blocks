using System;

namespace DomainBlocks.Projections.New.Builders;

public class EventCatchUpSubscriptionOptionsBuilder
{
    private Func<ProjectionRegistry, IEventDispatcher> _eventDispatcherFactory;
    private Func<ProjectionRegistry> _projectionRegistryFactory;

    public void AddProjection(Action<ProjectionOptionsBuilder> optionsAction)
    {
        var projectionOptionsBuilder = new ProjectionOptionsBuilder();
        optionsAction(projectionOptionsBuilder);
        WithProjectionRegistry(() => projectionOptionsBuilder.Build());
    }

    public void WithEventDispatcher(Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        _eventDispatcherFactory = eventDispatcherFactory;
    }

    public void WithProjectionRegistry(Func<ProjectionRegistry> projectionRegistryFactory)
    {
        _projectionRegistryFactory = projectionRegistryFactory;
    }

    public EventCatchUpSubscriptionOptions Build()
    {
        var projections = _projectionRegistryFactory();
        var eventDispatcher = _eventDispatcherFactory(projections);
        return new EventCatchUpSubscriptionOptions(eventDispatcher);
    }
}