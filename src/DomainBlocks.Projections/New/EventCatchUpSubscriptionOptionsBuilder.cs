using System;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptionsBuilder : IEventCatchUpSubscriptionOptionsBuilderInfrastructure
{
    public EventCatchUpSubscriptionOptions Options { get; private set; } = new();

    public EventCatchUpSubscriptionOptionsBuilder AddProjection(Action<ProjectionOptionsBuilder> optionsAction)
    {
        var projectionOptionsBuilder = new ProjectionOptionsBuilder();
        optionsAction(projectionOptionsBuilder);
        AddProjectionOptions(projectionOptionsBuilder.Options);
        return this;
    }

    public UsingResourceOptionsBuilder<TResource> Using<TResource>(Func<TResource> resourceFactory)
        where TResource : IDisposable
    {
        return new UsingResourceOptionsBuilder<TResource>(this, resourceFactory);
    }

    void IEventCatchUpSubscriptionOptionsBuilderInfrastructure.WithEventDispatcherFactory(
        Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        Options = Options.WithEventDispatcherFactory(eventDispatcherFactory);
    }

    internal void AddProjectionOptions(IProjectionOptions projectionOptions)
    {
        Options = Options.AddProjectionOptions(projectionOptions);
    }
}