using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ProjectionOptionsBuilder
{
    private readonly ProjectionOptions _options;

    public ProjectionOptionsBuilder(ProjectionOptions options)
    {
        _options = options;
        
        options.WithProjectionRegistryFactory(() =>
        {
            var eventProjectionMap = new EventProjectionMap();
            var projectionContextMap = new ProjectionContextMap();

            var projectionContext = new ProjectionContext(
                options.OnInitializing,
                options.OnCatchingUp,
                options.OnCaughtUp,
                options.OnEventDispatching,
                options.OnEventHandled);

            foreach (var (eventType, handler) in options.EventHandlers)
            {
                eventProjectionMap.AddProjectionFunc(eventType, handler);
                projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
            }

            return new ProjectionRegistry(eventProjectionMap, projectionContextMap, options.EventNameMap);
        });
    }

    public void OnInitializing(Func<CancellationToken, Task> onInitializing)
    {
        _options.WithOnInitializing(onInitializing);
    }

    public void OnCatchingUp(Func<CancellationToken, Task> onCatchingUp)
    {
        _options.WithOnCatchingUp(onCatchingUp);
    }

    public void OnCaughtUp(Func<CancellationToken, Task> onCaughtUp)
    {
        _options.WithOnCaughtUp(onCaughtUp);
    }

    public void OnEventDispatching(Func<CancellationToken, Task> onEventDispatching)
    {
        _options.WithOnEventDispatching(onEventDispatching);
    }

    public void OnEventHandled(Func<CancellationToken, Task> onEventHandled)
    {
        _options.WithOnEventHandled(onEventHandled);
    }

    public void When<TEvent>(Func<TEvent, Task> eventHandler)
    {
        _options.WithDefaultEventName<TEvent>();
        _options.WithEventHandler(eventHandler);
    }
}

public class ProjectionOptionsBuilder<TResource> where TResource : IDisposable
{
    public ProjectionOptionsBuilder(ProjectionOptions<TResource> options)
    {
        Options = options;
    }

    public ProjectionOptions<TResource> Options { get; }
}