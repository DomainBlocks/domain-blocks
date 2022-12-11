using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New.Internal;

internal sealed class ProjectionContext<TState> : IProjectionContext
{
    private readonly Func<IDisposable> _resourceFactory;
    private readonly Func<IDisposable, CatchUpSubscriptionStatus, TState> _contextFactory;
    private readonly Func<TState, CancellationToken, Task> _onInitializing;
    private readonly Func<TState, CancellationToken, Task<IStreamPosition>> _onSubscribing;
    private readonly Func<TState, IStreamPosition, CancellationToken, Task> _onSave;

    private CatchUpSubscriptionStatus _subscriptionStatus = CatchUpSubscriptionStatus.WarmingUp;
    private IDisposable _resource;
    private TState _state;

    public ProjectionContext(
        Func<IDisposable> resourceFactory,
        Func<IDisposable, CatchUpSubscriptionStatus, TState> contextFactory,
        Func<TState, CancellationToken, Task> onInitializing,
        Func<TState, CancellationToken, Task<IStreamPosition>> onSubscribing,
        Func<TState, IStreamPosition, CancellationToken, Task> onSave)
    {
        _resourceFactory = resourceFactory ?? throw new ArgumentNullException(nameof(resourceFactory));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _onInitializing = onInitializing ?? throw new ArgumentNullException(nameof(onInitializing));
        _onSubscribing = onSubscribing ?? throw new ArgumentNullException(nameof(onSubscribing));
        _onSave = onSave ?? throw new ArgumentNullException(nameof(onSave));
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        using var resource = _resourceFactory();
        var state = _contextFactory(resource, _subscriptionStatus);
        await _onInitializing(state, cancellationToken);
    }

    public async Task<IStreamPosition> OnSubscribing(CancellationToken cancellationToken = default)
    {
        using var resource = _resourceFactory();
        var state = _contextFactory(resource, _subscriptionStatus);
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

    internal RunProjection BindEventHandler(EventHandler<object, TState> handler)
    {
        return async (e, metadata, ct) =>
        {
            var eventRecord = new EventRecord<object>(e, metadata);
            await handler(eventRecord, _state, ct);
        };
    }

    private void BeginHandlingEvents()
    {
        _resource = _resourceFactory();
        _state = _contextFactory(_resource, _subscriptionStatus);
    }

    private async Task EndHandlingEvents(IStreamPosition position, CancellationToken cancellationToken)
    {
        if (_onSave == null)
        {
            throw new InvalidOperationException("Unable to save as no on-save action has been specified.");
        }

        await _onSave(_state, position, cancellationToken);

        _resource.Dispose();
        _resource = null;
        _state = default;
    }
}