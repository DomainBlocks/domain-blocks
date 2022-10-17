using System;

namespace DomainBlocks.Projections.New.Builders;

public class EventSubscriptionOptionsBuilder
{
    private IProjectionOptionsBuilder _projectionOptionsBuilder;
    private Func<ProjectionRegistry, IEventDispatcher> _eventDispatcherFactory;

    public void AddProjection<TState>(Action<ProjectionOptionsBuilder<TState>> optionsAction)
    {
        var projectionOptionsBuilder = new ProjectionOptionsBuilder<TState>();
        _projectionOptionsBuilder = projectionOptionsBuilder;
        optionsAction(projectionOptionsBuilder);
    }

    public void WithEventDispatcher(Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        _eventDispatcherFactory = eventDispatcherFactory;
    }

    public EventSubscriptionOptions Build()
    {
        var projections = _projectionOptionsBuilder.Build();
        var eventDispatcher = _eventDispatcherFactory(projections);
        return new EventSubscriptionOptions(eventDispatcher);
    }
}