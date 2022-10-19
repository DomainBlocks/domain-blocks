using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class StatelessProjectionOptions : ProjectionOptionsBase
{
    private readonly List<(Type, RunProjection)> _eventHandlers = new();

    public Func<CancellationToken, Task> OnInitializing { get; private set; }
    public Func<CancellationToken, Task> OnCatchingUp { get; private set; }
    public Func<CancellationToken, Task> OnCaughtUp { get; private set; }
    public Func<CancellationToken, Task> OnEventDispatching { get; private set; }
    public Func<CancellationToken, Task> OnEventHandled { get; private set; }
    public IEnumerable<(Type, RunProjection)> EventHandlers => _eventHandlers;
    
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
    
    public void WithEventHandler<TEvent>(Func<TEvent, Task> eventHandler)
    {
        _eventHandlers.Add((typeof(TEvent), (e, _) => eventHandler((TEvent)e)));
    }

    public override ProjectionRegistry ToProjectionRegistry()
    {
        var eventProjectionMap = new EventProjectionMap();
        var projectionContextMap = new ProjectionContextMap();

        var projectionContext = new StatelessProjectionContext(
            OnInitializing,
            OnCatchingUp,
            OnCaughtUp,
            OnEventDispatching,
            OnEventHandled);

        foreach (var (eventType, handler) in EventHandlers)
        {
            eventProjectionMap.AddProjectionFunc(eventType, handler);
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, EventNameMap);
    }
}