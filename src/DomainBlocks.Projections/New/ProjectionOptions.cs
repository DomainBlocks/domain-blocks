using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New.Internal;

namespace DomainBlocks.Projections.New;

public class ProjectionOptions<TState> : IProjectionOptions
{
    private readonly List<(Type, IEventHandlerInvoker<TState>)> _eventHandlerInvokers = new();
    private readonly List<IEventHandlerInterceptor> _eventHandlerInterceptors = new();
    private Func<IDisposable> _resourceFactory;
    private Func<IDisposable, CatchUpSubscriptionStatus, TState> _stateFactory;
    private Func<TState, CancellationToken, Task> _onInitializing;
    private Func<TState, CancellationToken, Task<IStreamPosition>> _onSubscribing;
    private Func<TState, IStreamPosition, CancellationToken, Task> _onSave;

    public ProjectionOptions()
    {
        _onInitializing = (_, _) => Task.CompletedTask;
        _onSubscribing = (_, _) => Task.FromResult(StreamPosition.Empty);
    }

    private ProjectionOptions(ProjectionOptions<TState> copyFrom)
    {
        _eventHandlerInvokers = copyFrom._eventHandlerInvokers.ToList();
        _eventHandlerInterceptors = copyFrom._eventHandlerInterceptors.ToList();
        _resourceFactory = copyFrom._resourceFactory;
        _stateFactory = copyFrom._stateFactory;
        _onInitializing = copyFrom._onInitializing;
        _onSubscribing = copyFrom._onSubscribing;
        _onSave = copyFrom._onSave;
        _resourceFactory = copyFrom._resourceFactory;
    }

    public ProjectionOptions<TState> WithStateFactory(Func<CatchUpSubscriptionStatus, TState> stateFactory)
    {
        if (stateFactory == null) throw new ArgumentNullException(nameof(stateFactory));

        return new ProjectionOptions<TState>(this)
        {
            _resourceFactory = () => null,
            _stateFactory = (_, status) => stateFactory(status)
        };
    }

    public ProjectionOptions<TState> WithStateFactory<TResource>(
        Func<TResource> resourceFactory,
        Func<TResource, CatchUpSubscriptionStatus, TState> stateFactory) where TResource : IDisposable
    {
        if (resourceFactory == null) throw new ArgumentNullException(nameof(resourceFactory));
        if (stateFactory == null) throw new ArgumentNullException(nameof(stateFactory));

        return new ProjectionOptions<TState>(this)
        {
            _resourceFactory = () => resourceFactory(),
            _stateFactory = (d, s) => stateFactory((TResource)d, s)
        };
    }

    public ProjectionOptions<TState> WithOnInitializing(Func<TState, CancellationToken, Task> onInitializing)
    {
        if (onInitializing == null) throw new ArgumentNullException(nameof(onInitializing));
        return new ProjectionOptions<TState>(this) { _onInitializing = onInitializing };
    }

    public ProjectionOptions<TState> WithOnSubscribing(
        Func<TState, CancellationToken, Task<IStreamPosition>> onSubscribing)
    {
        if (onSubscribing == null) throw new ArgumentNullException(nameof(onSubscribing));
        return new ProjectionOptions<TState>(this) { _onSubscribing = onSubscribing };
    }

    public ProjectionOptions<TState> WithOnSave(Func<TState, IStreamPosition, CancellationToken, Task> onSave)
    {
        if (onSave == null) throw new ArgumentNullException(nameof(onSave));
        return new ProjectionOptions<TState>(this) { _onSave = onSave };
    }

    public ProjectionOptions<TState> WithHandler<TEvent>(EventHandler<TEvent, TState> handler)
    {
        var copy = new ProjectionOptions<TState>(this);
        var invoker = EventHandlerInvoker.Create(handler).Intercept(_eventHandlerInterceptors);
        copy._eventHandlerInvokers.Add((typeof(TEvent), invoker));
        return copy;
    }

    public ProjectionOptions<TState> WithHandler<TEvent>(Action<IEventRecord<TEvent>, TState> handler)
    {
        return WithHandler<TEvent>((e, s, _) =>
        {
            handler(e, s);
            return Task.CompletedTask;
        });
    }

    public ProjectionOptions<TState> WithHandler<TEvent>(Func<TEvent, TState, CancellationToken, Task> handler)
    {
        return WithHandler<TEvent>((e, s, ct) => handler(e.Event, s, ct));
    }

    public ProjectionOptions<TState> WithHandler<TEvent>(Action<TEvent, TState> handler)
    {
        return WithHandler<TEvent>((e, s, _) =>
        {
            handler(e.Event, s);
            return Task.CompletedTask;
        });
    }

    public ProjectionOptions<TState> WithInterceptors(IEnumerable<IEventHandlerInterceptor> interceptors)
    {
        var copy = new ProjectionOptions<TState>(this);

        var interceptorArray = interceptors as IEventHandlerInterceptor[] ?? interceptors.ToArray();
        copy._eventHandlerInterceptors.AddRange(interceptorArray);

        // Apply the interceptors to any existing invokers.
        var invokers = copy._eventHandlerInvokers.Select(x =>
        {
            var (type, invoker) = x;
            return (type, invoker.Intercept(interceptorArray));
        });

        copy._eventHandlerInvokers.Clear();
        copy._eventHandlerInvokers.AddRange(invokers);

        return copy;
    }

    public ProjectionRegistry Register(ProjectionRegistry registry)
    {
        var projectionContext = new ProjectionContext<TState>(
            _resourceFactory, _stateFactory, _onInitializing, _onSubscribing, _onSave);

        foreach (var (eventType, invoker) in _eventHandlerInvokers)
        {
            var projectionFunc = projectionContext.BindEventHandler(invoker.Invoke);

            registry = registry
                .RegisterDefaultEventName(eventType)
                .AddProjectionFunc(eventType, projectionFunc)
                .RegisterProjectionContext(eventType, projectionContext);
        }

        return registry;
    }
}