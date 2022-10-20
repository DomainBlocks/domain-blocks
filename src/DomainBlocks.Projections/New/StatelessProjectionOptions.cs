using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class StatelessProjectionOptions : IProjectionOptions
{
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, RunProjection)> _eventHandlers = new();
    private Func<CancellationToken, Task> _onInitializing;
    private Func<CancellationToken, Task> _onCatchingUp;
    private Func<CancellationToken, Task> _onCaughtUp;
    private Func<CancellationToken, Task> _onEventDispatching;
    private Func<CancellationToken, Task> _onEventHandled;

    public StatelessProjectionOptions()
    {
    }

    private StatelessProjectionOptions(StatelessProjectionOptions copyFrom)
    {
        _eventNameMap = new ProjectionEventNameMap(copyFrom._eventNameMap);
        _eventHandlers = new List<(Type, RunProjection)>(copyFrom._eventHandlers);
        _onInitializing = copyFrom._onInitializing;
        _onCatchingUp = copyFrom._onCatchingUp;
        _onCaughtUp = copyFrom._onCaughtUp;
        _onEventDispatching = copyFrom._onEventDispatching;
        _onEventHandled = copyFrom._onEventHandled;
    }

    public StatelessProjectionOptions WithOnInitializing(Func<CancellationToken, Task> onInitializing)
    {
        return new StatelessProjectionOptions(this) { _onInitializing = onInitializing };
    }

    public StatelessProjectionOptions WithOnCatchingUp(Func<CancellationToken, Task> onCatchingUp)
    {
        return new StatelessProjectionOptions(this) { _onCatchingUp = onCatchingUp };
    }

    public StatelessProjectionOptions WithOnCaughtUp(Func<CancellationToken, Task> onCaughtUp)
    {
        return new StatelessProjectionOptions(this) { _onCaughtUp = onCaughtUp };
    }

    public StatelessProjectionOptions WithOnEventDispatching(Func<CancellationToken, Task> onEventDispatching)
    {
        return new StatelessProjectionOptions(this) { _onEventDispatching = onEventDispatching };
    }

    public StatelessProjectionOptions WithOnEventHandled(Func<CancellationToken, Task> onEventHandled)
    {
        return new StatelessProjectionOptions(this) { _onEventHandled = onEventHandled };
    }

    public StatelessProjectionOptions WithEventHandler<TEvent>(Func<TEvent, Task> eventHandler)
    {
        var copy = new StatelessProjectionOptions(this);
        copy._eventNameMap.RegisterDefaultEventName<TEvent>();
        copy._eventHandlers.Add((typeof(TEvent), (e, _) => eventHandler((TEvent)e)));
        return copy;
    }

    public ProjectionRegistry ToProjectionRegistry()
    {
        var eventProjectionMap = new EventProjectionMap();
        var projectionContextMap = new ProjectionContextMap();

        var projectionContext = new StatelessProjectionContext(
            _onInitializing,
            _onCatchingUp,
            _onCaughtUp,
            _onEventDispatching,
            _onEventHandled);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            eventProjectionMap.AddProjectionFunc(eventType, handler);
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, _eventNameMap);
    }
}