using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public sealed class ResourceProjectionContext<TResource> : IProjectionContext where TResource : IDisposable
{
    private readonly Func<TResource> _resourceFactory;
    private readonly Func<TResource, CancellationToken, Task> _onInitializing;
    private readonly Func<TResource, CancellationToken, Task<IStreamPosition>> _onSubscribing;
    private readonly Func<TResource, IStreamPosition, CancellationToken, Task> _onSave;
    private CatchUpSubscriptionStatus _subscriptionStatus;
    private EventHandlerContext<TResource> _eventHandlerContext;

    public ResourceProjectionContext(
        Func<TResource> resourceFactory,
        Func<TResource, CancellationToken, Task> onInitializing,
        Func<TResource, CancellationToken, Task<IStreamPosition>> onSubscribing,
        Func<TResource, IStreamPosition, CancellationToken, Task> onSave)
    {
        _resourceFactory = resourceFactory ?? throw new ArgumentNullException(nameof(resourceFactory));
        _onInitializing = onInitializing ?? throw new ArgumentNullException(nameof(onInitializing));
        _onSubscribing = onSubscribing ?? throw new ArgumentNullException(nameof(onSubscribing));
        _onSave = onSave ?? throw new ArgumentNullException(nameof(onSave));
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        using var resource = _resourceFactory();
        await _onInitializing(resource, cancellationToken);
    }

    public async Task<IStreamPosition> OnSubscribing(CancellationToken cancellationToken = default)
    {
        using var resource = _resourceFactory();
        return await _onSubscribing(resource, cancellationToken);
    }

    public Task OnCatchingUp(CancellationToken cancellationToken = default)
    {
        _subscriptionStatus = CatchUpSubscriptionStatus.CatchingUp;
        BeginHandlingEvents();
        return Task.CompletedTask;
    }

    public async Task OnCaughtUp(IStreamPosition position, CancellationToken cancellationToken = default)
    {
        _subscriptionStatus = CatchUpSubscriptionStatus.Live;
        await EndHandlingEvents(position, cancellationToken);
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
        Func<object, EventHandlerContext<TResource>, CancellationToken, Task> projection)
    {
        return (e, metadata, ct) =>
        {
            _eventHandlerContext.Metadata = metadata;
            return projection(e, _eventHandlerContext, ct);
        };
    }

    private void BeginHandlingEvents()
    {
        if (_resourceFactory == null)
        {
            throw new InvalidOperationException(
                $"Unable to create resource of type {typeof(TResource).Name} " +
                "as no resource factory has been specified.");
        }

        var resource = _resourceFactory();
        _eventHandlerContext = new EventHandlerContext<TResource>(_subscriptionStatus, resource);
    }

    private async Task EndHandlingEvents(IStreamPosition position, CancellationToken cancellationToken)
    {
        if (_onSave == null)
        {
            throw new InvalidOperationException("Unable to save as no on-save action has been specified.");
        }

        await _onSave(_eventHandlerContext.Resource, position, cancellationToken);
        await _eventHandlerContext.RunOnSavedActions(cancellationToken);
        _eventHandlerContext.Resource.Dispose();
        _eventHandlerContext = null;
    }
}