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
    private Func<CancellationToken, Task> _onInitializing;
    private Func<CancellationToken, Task<object>> _onCatchingUp;
    private Func<object, CancellationToken, Task> _onCaughtUp;
    private Func<object, TState> _catchUpContextToStateSelector;
    private Func<CancellationToken, Task<object>> _onEventDispatching;
    private Func<object, CancellationToken, Task> _onEventHandled;
    private Func<object, TState> _eventContextToStateSelector;
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, Func<object, EventMetadata, TState, Task>)> _eventHandlers = new();

    public void OnInitializing(Func<CancellationToken, Task> onStarting)
    {
        _onInitializing = onStarting;
    }

    public CatchUpContext<TContext> OnCatchingUp<TContext>(Func<CancellationToken, Task<TContext>> onCatchingUp)
    {
        _onCatchingUp = async ct =>
        {
            var state = await onCatchingUp(ct).ConfigureAwait(false);
            return state;
        };

        return new CatchUpContext<TContext>(this);
    }

    public EventContext<TContext> OnEventDispatching<TContext>(
        Func<CancellationToken, Task<TContext>> onEventDispatching)
    {
        _onEventDispatching = async ct =>
        {
            var state = await onEventDispatching(ct).ConfigureAwait(false);
            return state;
        };

        return new EventContext<TContext>(this);
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
            _onInitializing,
            _onCatchingUp,
            _onCaughtUp,
            _onEventDispatching,
            _onEventHandled,
            _catchUpContextToStateSelector,
            _eventContextToStateSelector);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            var projectionFunc = projectionContext.CreateProjectionFunc(handler);
            eventProjectionMap.AddProjectionFunc(eventType, projectionFunc);
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, _eventNameMap);
    }

    public class CatchUpContext<TContext>
    {
        private readonly ProjectionOptionsBuilder<TState> _parent;

        public CatchUpContext(ProjectionOptionsBuilder<TState> parent)
        {
            _parent = parent;
        }

        public CatchUpContext<TContext> OnCaughtUp(Func<TContext, CancellationToken, Task> onCaughtUp)
        {
            _parent._onCaughtUp = (context, ct) => onCaughtUp((TContext)context, ct);
            return this;
        }

        public void WithState(Func<TContext, TState> stateSelector)
        {
            _parent._catchUpContextToStateSelector = x => stateSelector((TContext)x);
        }
    }

    public class EventContext<TContext>
    {
        private readonly ProjectionOptionsBuilder<TState> _parent;

        public EventContext(ProjectionOptionsBuilder<TState> parent)
        {
            _parent = parent;
        }

        public EventContext<TContext> OnEventHandled(Func<TContext, CancellationToken, Task> onCaughtUp)
        {
            _parent._onEventHandled = (context, ct) => onCaughtUp((TContext)context, ct);
            return this;
        }

        public void WithState(Func<TContext, TState> stateSelector)
        {
            _parent._eventContextToStateSelector = x => stateSelector((TContext)x);
        }
    }
}