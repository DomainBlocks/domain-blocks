using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public sealed class StatefulProjectionContext<TResource, TState> : IProjectionContext where TResource : IDisposable
{
    private readonly Func<TResource> _resourceFactory;
    private readonly Func<TResource, CatchUpSubscriptionStatus, TState> _stateFactory;
    private readonly Func<TState, CancellationToken, Task> _onInitializing;
    private readonly Func<TState, CancellationToken, Task<IStreamPosition>> _onSubscribing;
    private readonly Func<TState, IStreamPosition, CancellationToken, Task> _onSave;
    private CatchUpSubscriptionStatus _subscriptionStatus = CatchUpSubscriptionStatus.WarmingUp;
    private TResource _resource;
    private EventHandlerContext<TState> _eventHandlerContext;

    public StatefulProjectionContext(
        Func<TResource> resourceFactory,
        Func<TResource, CatchUpSubscriptionStatus, TState> stateFactory,
        Func<TState, CancellationToken, Task> onInitializing,
        Func<TState, CancellationToken, Task<IStreamPosition>> onSubscribing,
        Func<TState, IStreamPosition, CancellationToken, Task> onSave)
    {
        _resourceFactory = resourceFactory ?? throw new ArgumentNullException(nameof(resourceFactory));
        _stateFactory = stateFactory ?? throw new ArgumentNullException(nameof(stateFactory));
        _onInitializing = onInitializing ?? throw new ArgumentNullException(nameof(onInitializing));
        _onSubscribing = onSubscribing ?? throw new ArgumentNullException(nameof(onSubscribing));
        _onSave = onSave ?? throw new ArgumentNullException(nameof(onSave));
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        using var resource = _resourceFactory();
        var state = _stateFactory(resource, _subscriptionStatus);
        await _onInitializing(state, cancellationToken);
    }

    public async Task<IStreamPosition> OnSubscribing(CancellationToken cancellationToken = default)
    {
        using var resource = _resourceFactory();
        var state = _stateFactory(resource, _subscriptionStatus);
        return await _onSubscribing(state, cancellationToken);
    }

    public Task OnCatchingUp(CancellationToken cancellationToken = default)
    {
        if (_subscriptionStatus == CatchUpSubscriptionStatus.Live)
        {
            _subscriptionStatus = CatchUpSubscriptionStatus.CatchingUp;
        }

        BeginHandlingEvents();
        return Task.CompletedTask;
    }

    public async Task OnCaughtUp(IStreamPosition position, CancellationToken cancellationToken = default)
    {
        await EndHandlingEvents(position, cancellationToken);
        _subscriptionStatus = CatchUpSubscriptionStatus.Live;
    }

    public Task OnEventDispatching(CancellationToken cancellationToken = default)
    {
        if (_subscriptionStatus == CatchUpSubscriptionStatus.Live)
        {
            BeginHandlingEvents();
        }

        return Task.CompletedTask;
    }

    public async Task OnEventHandled(IStreamPosition position, CancellationToken cancellationToken = default)
    {
        if (_subscriptionStatus == CatchUpSubscriptionStatus.Live)
        {
            await EndHandlingEvents(position, cancellationToken);
        }
    }

    internal RunProjection BindProjection(
        Func<object, IEventHandlerContext<TState>, CancellationToken, Task> projection)
    {
        return (e, metadata, ct) =>
        {
            _eventHandlerContext.Metadata = metadata;
            return projection(e, _eventHandlerContext, ct);
        };
    }

    private void BeginHandlingEvents()
    {
        _resource = _resourceFactory();
        var state = _stateFactory(_resource, _subscriptionStatus);
        _eventHandlerContext = new EventHandlerContext<TState>(_subscriptionStatus, state);
    }

    private async Task EndHandlingEvents(IStreamPosition position, CancellationToken cancellationToken)
    {
        if (_onSave == null)
        {
            throw new InvalidOperationException("Unable to save as no on-save action has been specified.");
        }

        await _onSave(_eventHandlerContext.State, position, cancellationToken);
        await _eventHandlerContext.RunOnSavedActions(cancellationToken);
        _resource.Dispose();
        _eventHandlerContext = null;
    }
}