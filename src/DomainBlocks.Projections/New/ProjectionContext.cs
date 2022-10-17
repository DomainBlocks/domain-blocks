using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.New;

internal class ProjectionContext<TState> : IProjectionContext
{
    private readonly Func<CancellationToken, Task<TState>> _onSubscribing;
    private readonly Func<TState, CancellationToken, Task> _onCaughtUp;
    private readonly Func<CancellationToken, Task<TState>> _onEventHandling;
    private readonly Func<TState, CancellationToken, Task> _onEventHandled;
    private bool _isCaughtUp;
    private TState _state;

    public ProjectionContext(
        Func<CancellationToken, Task<TState>> onSubscribing,
        Func<TState, CancellationToken, Task> onCaughtUp,
        Func<CancellationToken, Task<TState>> onEventHandling,
        Func<TState, CancellationToken, Task> onEventHandled)
    {
        _onSubscribing = onSubscribing;
        _onCaughtUp = onCaughtUp;
        _onEventHandling = onEventHandling;
        _onEventHandled = onEventHandled;
    }

    public async Task OnSubscribing(CancellationToken cancellationToken = default)
    {
        _state = await _onSubscribing.Invoke(cancellationToken);
    }

    public async Task OnCaughtUp(CancellationToken cancellationToken = default)
    {
        if (_isCaughtUp) return;
        await _onCaughtUp(_state, cancellationToken);
        _state = default;
        _isCaughtUp = true;
    }

    public async Task OnBeforeHandleEvent(CancellationToken cancellationToken = default)
    {
        if (!_isCaughtUp) return;
        _state = await _onEventHandling(cancellationToken);
    }

    public async Task OnAfterHandleEvent(CancellationToken cancellationToken = default)
    {
        if (!_isCaughtUp) return;
        await _onEventHandled(_state, cancellationToken);
        _state = default;
    }

    public RunProjection CreateProjectionFunc(Func<object, EventMetadata, TState, Task> eventHandler)
    {
        return (e, metadata) => eventHandler(e, metadata, _state);
    }
}