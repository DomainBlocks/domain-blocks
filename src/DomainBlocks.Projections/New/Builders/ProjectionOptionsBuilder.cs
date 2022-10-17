using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.New.Builders;

public interface IProjectionOptionsBuilder
{
    public ProjectionRegistry Build();
}

public class ProjectionOptionsBuilder<TState> : IProjectionOptionsBuilder
{
    private Func<CancellationToken, Task<TState>> _onSubscribing;
    private Func<TState, CancellationToken, Task> _onCaughtUp;
    private Func<CancellationToken, Task<TState>> _onEventHandling;
    private Func<TState, CancellationToken, Task> _onEventHandled;
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, Func<object, EventMetadata, TState, Task>)> _eventHandlers = new();

    public void OnSubscribing(Func<CancellationToken, Task<TState>> onSubscribing)
    {
        _onSubscribing = onSubscribing;
    }

    public void OnCaughtUp(Func<TState, CancellationToken, Task> onCaughtUp)
    {
        _onCaughtUp = onCaughtUp;
    }

    public void OnEventHandling(Func<CancellationToken, Task<TState>> onEventHandling)
    {
        _onEventHandling = onEventHandling;
    }

    public void OnEventHandled(Func<TState, CancellationToken, Task> onEventHandled)
    {
        _onEventHandled = onEventHandled;
    }

    public void When<TEvent>(Action<TEvent, TState> eventHandler)
    {
        _eventNameMap.RegisterDefaultEventName<TEvent>();

        _eventHandlers.Add((typeof(TEvent), (e, _, state) =>
        {
            eventHandler((TEvent)e, state);
            return Task.CompletedTask;
        }));
    }

    public void WhenAsync<TEvent>(Func<TEvent, TState, Task> eventHandler)
    {
        _eventNameMap.RegisterDefaultEventName<TEvent>();
        _eventHandlers.Add((typeof(TEvent), (e, _, state) => eventHandler((TEvent)e, state)));
    }

    // For now we return a ProjectionRegistry as an interim solution.
    public ProjectionRegistry Build()
    {
        var eventProjectionMap = new EventProjectionMap();
        var projectionContextMap = new ProjectionContextMap();

        var projectionContext = new ProjectionContext<TState>(
            _onSubscribing, _onCaughtUp, _onEventHandling, _onEventHandled);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            eventProjectionMap.AddProjectionFunc(eventType, projectionContext.CreateProjectionFunc(handler));
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, _eventNameMap);
    }
}