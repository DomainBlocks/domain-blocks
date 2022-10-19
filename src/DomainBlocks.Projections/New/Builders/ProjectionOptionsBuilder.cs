using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New.Builders;

public interface IProjectionOptionsBuilder
{
    public ProjectionRegistry Build();
}

public class ProjectionOptionsBuilder : IProjectionOptionsBuilder
{
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, RunProjection)> _eventHandlers = new();
    private Func<CancellationToken, Task> _onInitializing;
    private Func<CancellationToken, Task> _onCatchingUp;
    private Func<CancellationToken, Task> _onCaughtUp;
    private Func<CancellationToken, Task> _onEventDispatching;
    private Func<CancellationToken, Task> _onEventHandled;

    public void OnInitializing(Func<CancellationToken, Task> onInitializing)
    {
        _onInitializing = onInitializing;
    }

    public void OnCatchingUp(Func<CancellationToken, Task> onCatchingUp)
    {
        _onCatchingUp = onCatchingUp;
    }

    public void OnCaughtUp(Func<CancellationToken, Task> onCaughtUp)
    {
        _onCaughtUp = onCaughtUp;
    }

    public void OnEventDispatching(Func<CancellationToken, Task> onEventDispatching)
    {
        _onEventDispatching = onEventDispatching;
    }

    public void OnEventHandled(Func<CancellationToken, Task> onEventHandled)
    {
        _onEventHandled = onEventHandled;
    }

    public void When<TEvent>(Func<TEvent, Task> eventHandler)
    {
        _eventNameMap.RegisterDefaultEventName<TEvent>();
        _eventHandlers.Add((typeof(TEvent), (e, _) => eventHandler((TEvent)e)));
    }

    public ProjectionRegistry Build()
    {
        var eventProjectionMap = new EventProjectionMap();
        var projectionContextMap = new ProjectionContextMap();

        var projectionContext = new ProjectionContext(
            _onInitializing, _onCatchingUp, _onCaughtUp, _onEventDispatching, _onEventHandled);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            eventProjectionMap.AddProjectionFunc(eventType, handler);
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, _eventNameMap);
    }
}