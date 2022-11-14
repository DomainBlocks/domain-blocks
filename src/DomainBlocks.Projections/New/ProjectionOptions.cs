using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ProjectionOptions : IProjectionOptions
{
    private readonly List<(Type, RunProjection)> _eventHandlers = new();
    private Func<CancellationToken, Task> _onInitializing;
    private Func<CancellationToken, Task> _onCatchingUp;
    private Func<CancellationToken, Task> _onCaughtUp;
    private Func<CancellationToken, Task> _onEventDispatching;
    private Func<CancellationToken, Task> _onEventHandled;

    public ProjectionOptions()
    {
    }

    private ProjectionOptions(ProjectionOptions copyFrom)
    {
        _eventHandlers = new List<(Type, RunProjection)>(copyFrom._eventHandlers);
        _onInitializing = copyFrom._onInitializing ?? (_ => Task.CompletedTask);
        _onCatchingUp = copyFrom._onCatchingUp ?? (_ => Task.CompletedTask);
        _onCaughtUp = copyFrom._onCaughtUp ?? (_ => Task.CompletedTask);
        _onEventDispatching = copyFrom._onEventDispatching ?? (_ => Task.CompletedTask);
        _onEventHandled = copyFrom._onEventHandled ?? (_ => Task.CompletedTask);
    }

    public ProjectionOptions WithOnInitializing(Func<CancellationToken, Task> onInitializing)
    {
        return new ProjectionOptions(this) { _onInitializing = onInitializing };
    }

    public ProjectionOptions WithOnCatchingUp(Func<CancellationToken, Task> onCatchingUp)
    {
        return new ProjectionOptions(this) { _onCatchingUp = onCatchingUp };
    }

    public ProjectionOptions WithOnCaughtUp(Func<CancellationToken, Task> onCaughtUp)
    {
        return new ProjectionOptions(this) { _onCaughtUp = onCaughtUp };
    }

    public ProjectionOptions WithOnEventDispatching(Func<CancellationToken, Task> onEventDispatching)
    {
        return new ProjectionOptions(this) { _onEventDispatching = onEventDispatching };
    }

    public ProjectionOptions WithOnEventHandled(Func<CancellationToken, Task> onEventHandled)
    {
        return new ProjectionOptions(this) { _onEventHandled = onEventHandled };
    }

    public ProjectionOptions WithEventHandler<TEvent>(Func<TEvent, Task> eventHandler)
    {
        var copy = new ProjectionOptions(this);
        copy._eventHandlers.Add((typeof(TEvent), (e, _) => eventHandler((TEvent)e)));
        return copy;
    }

    public ProjectionRegistry Register(ProjectionRegistry registry)
    {
        var projectionContext = new ProjectionContext(
            _onInitializing,
            _onCatchingUp,
            _onCaughtUp,
            _onEventDispatching,
            _onEventHandled);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            registry = registry
                .RegisterDefaultEventName(eventType)
                .AddProjectionFunc(eventType, handler)
                .RegisterProjectionContext(eventType, projectionContext);
        }

        return registry;
    }
}