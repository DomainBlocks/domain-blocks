using System;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptionsBuilder
{
    public EventCatchUpSubscriptionOptions Options { get; private set; } = new();

    public void AddProjection(Action<StatelessProjectionOptionsBuilder> optionsAction)
    {
        var projectionOptionsBuilder = new StatelessProjectionOptionsBuilder();
        optionsAction(projectionOptionsBuilder);
        Options = Options.WithProjectionOptions(projectionOptionsBuilder.Options);
    }

    public ProjectionResourceBuilder<TResource> Using<TResource>(Func<TResource> resourceFactory)
        where TResource : IDisposable
    {
        var builder = new ProjectionResourceBuilder<TResource>(resourceFactory);
        Options = Options.WithProjectionOptionsProvider(builder);
        return builder;
    }

    public void WithEventDispatcherFactory(Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        Options = Options.WithEventDispatcherFactory(eventDispatcherFactory);
    }
}