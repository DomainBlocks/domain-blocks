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
    private Func<TState, CancellationToken, Task> _onSubscribing;
    private Func<CancellationToken,Task<TState>> _onUpdating;
    private Func<TState,CancellationToken,Task> _onUpdated;
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, Func<object, EventMetadata, TState, Task>)> _eventHandlers = new();

    public void OnSubscribing(Func<TState, CancellationToken, Task> onSubscribing)
    {
        _onSubscribing = onSubscribing;
    }
    
    public void OnUpdating(Func<CancellationToken, Task<TState>> onUpdating)
    {
        _onUpdating = onUpdating;
    }
    
    public void OnUpdated(Func<TState, CancellationToken, Task> onUpdated)
    {
        _onUpdated = onUpdated;
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
        var projectionContext = new ProjectionContext<TState>(_onSubscribing, _onUpdating, _onUpdated);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            eventProjectionMap.AddProjectionFunc(eventType, projectionContext.CreateProjectionFunc(handler));
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, _eventNameMap);
    }
}