using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class StatefulProjectionOptions<TResource, TState> : IProjectionOptions where TResource : IDisposable
{
    private readonly List<(Type, Func<object, IEventHandlerContext<TState>, CancellationToken, Task>)> _projections =
        new();

    private Func<TResource> _resourceFactory;
    private Func<TResource, CatchUpSubscriptionStatus, TState> _stateFactory;
    private Func<TState, CancellationToken, Task> _onInitializing;
    private Func<TState, CancellationToken, Task<IStreamPosition>> _onSubscribing;
    private Func<TState, IStreamPosition, CancellationToken, Task> _onSave;

    public StatefulProjectionOptions()
    {
        _onInitializing = (_, _) => Task.CompletedTask;
        _onSubscribing = (_, _) => Task.FromResult(StreamPosition.Empty);
    }

    private StatefulProjectionOptions(StatefulProjectionOptions<TResource, TState> copyFrom)
    {
        _projections = copyFrom._projections.ToList();
        _resourceFactory = copyFrom._resourceFactory;
        _stateFactory = copyFrom._stateFactory;
        _onInitializing = copyFrom._onInitializing;
        _onSubscribing = copyFrom._onSubscribing;
        _onSave = copyFrom._onSave;
        _resourceFactory = copyFrom._resourceFactory;
    }

    public StatefulProjectionOptions<TResource, TState> WithResourceFactory(Func<TResource> resourceFactory)
    {
        if (resourceFactory == null) throw new ArgumentNullException(nameof(resourceFactory));
        return new StatefulProjectionOptions<TResource, TState>(this) { _resourceFactory = resourceFactory };
    }

    public StatefulProjectionOptions<TResource, TState> WithStateFactory(
        Func<TResource, CatchUpSubscriptionStatus, TState> stateFactory)
    {
        if (stateFactory == null) throw new ArgumentNullException(nameof(stateFactory));
        return new StatefulProjectionOptions<TResource, TState>(this) { _stateFactory = stateFactory };
    }

    public StatefulProjectionOptions<TResource, TState> WithOnInitializing(
        Func<TState, CancellationToken, Task> onInitializing)
    {
        if (onInitializing == null) throw new ArgumentNullException(nameof(onInitializing));
        return new StatefulProjectionOptions<TResource, TState>(this) { _onInitializing = onInitializing };
    }

    public StatefulProjectionOptions<TResource, TState> WithOnSubscribing(
        Func<TState, CancellationToken, Task<IStreamPosition>> onSubscribing)
    {
        if (onSubscribing == null) throw new ArgumentNullException(nameof(onSubscribing));
        return new StatefulProjectionOptions<TResource, TState>(this) { _onSubscribing = onSubscribing };
    }

    public StatefulProjectionOptions<TResource, TState> WithOnSave(
        Func<TState, IStreamPosition, CancellationToken, Task> onSave)
    {
        if (onSave == null) throw new ArgumentNullException(nameof(onSave));
        return new StatefulProjectionOptions<TResource, TState>(this) { _onSave = onSave };
    }

    public StatefulProjectionOptions<TResource, TState> WithProjection<TEvent>(
        Func<TEvent, IEventHandlerContext<TState>, CancellationToken, Task> projection)
    {
        var copy = new StatefulProjectionOptions<TResource, TState>(this);
        copy._projections.Add((typeof(TEvent), (e, context, ct) => projection((TEvent)e, context, ct)));
        return copy;
    }

    public StatefulProjectionOptions<TResource, TState> WithProjection<TEvent>(
        Action<TEvent, IEventHandlerContext<TState>> projection)
    {
        return WithProjection<TEvent>((e, context, _) =>
        {
            projection(e, context);
            return Task.CompletedTask;
        });
    }

    public ProjectionRegistry Register(ProjectionRegistry registry)
    {
        var projectionContext = new StatefulProjectionContext<TResource, TState>(
            _resourceFactory, _stateFactory, _onInitializing, _onSubscribing, _onSave);

        foreach (var (eventType, projection) in _projections)
        {
            var projectionFunc = projectionContext.BindProjection(projection);

            registry = registry
                .RegisterDefaultEventName(eventType)
                .AddProjectionFunc(eventType, projectionFunc)
                .RegisterProjectionContext(eventType, projectionContext);
        }

        return registry;
    }
}