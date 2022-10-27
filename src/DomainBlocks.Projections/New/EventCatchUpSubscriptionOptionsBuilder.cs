using System;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptionsBuilder
{
    public EventCatchUpSubscriptionOptions Options { get; private set; } = new();

    public void AddProjection(Action<ProjectionOptionsBuilder> optionsAction)
    {
        var projectionOptionsBuilder = new ProjectionOptionsBuilder();
        optionsAction(projectionOptionsBuilder);
        AddProjectionOptions(projectionOptionsBuilder.Options);
    }

    public UsingResourceOptionsBuilder<TResource> Using<TResource>(Func<TResource> resourceFactory)
        where TResource : IDisposable
    {
        return new UsingResourceOptionsBuilder<TResource>(this, resourceFactory);
    }

    public void WithEventDispatcherFactory(Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        Options = Options.WithEventDispatcherFactory(eventDispatcherFactory);
    }

    public void AddProjectionOptions(IProjectionOptions projectionOptions)
    {
        Options = Options.AddProjectionOptions(projectionOptions);
    }
}