using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ProjectionOptions
{
    private readonly List<(Type, RunProjection)> _eventHandlers = new();

    public Func<CancellationToken, Task> OnInitializing { get; private set; }
    public Func<CancellationToken, Task> OnCatchingUp { get; private set; }
    public Func<CancellationToken, Task> OnCaughtUp { get; private set; }
    public Func<CancellationToken, Task> OnEventDispatching { get; private set; }
    public Func<CancellationToken, Task> OnEventHandled { get; private set; }
    public ProjectionEventNameMap EventNameMap { get; } = new();
    public IEnumerable<(Type, RunProjection)> EventHandlers => _eventHandlers;
    public Func<ProjectionRegistry> ProjectionRegistryFactory { get; private set; }

    public void WithOnInitializing(Func<CancellationToken, Task> onInitializing)
    {
        OnInitializing = onInitializing;
    }

    public void WithOnCatchingUp(Func<CancellationToken, Task> onInitializing)
    {
        OnCatchingUp = onInitializing;
    }

    public void WithOnCaughtUp(Func<CancellationToken, Task> onCaughtUp)
    {
        OnCaughtUp = onCaughtUp;
    }

    public void WithOnEventDispatching(Func<CancellationToken, Task> onEventDispatching)
    {
        OnEventDispatching = onEventDispatching;
    }

    public void WithOnEventHandled(Func<CancellationToken, Task> onEventHandled)
    {
        OnEventHandled = onEventHandled;
    }

    public void WithDefaultEventName<TEvent>()
    {
        EventNameMap.RegisterDefaultEventName<TEvent>();
    }

    public void WithEventHandler<TEvent>(Func<TEvent, Task> eventHandler)
    {
        _eventHandlers.Add((typeof(TEvent), (e, _) => eventHandler((TEvent)e)));
    }

    public void WithProjectionRegistryFactory(Func<ProjectionRegistry> projectionRegistryFactory)
    {
        ProjectionRegistryFactory = projectionRegistryFactory;
    }
}

public class ProjectionOptions<TResource> : ProjectionOptions where TResource : IDisposable
{
    public Func<TResource> ResourceFactory { get; private set; }

    public void WithResourceFactory(Func<TResource> resourceFactory)
    {
        ResourceFactory = resourceFactory;
    }
}