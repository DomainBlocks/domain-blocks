using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.New;

internal class ProjectionContext<TState> : IProjectionContext
{
    private readonly Func<TState, CancellationToken, Task> _onSubscribing;
    private readonly Func<CancellationToken, Task<TState>> _onProjecting;
    private readonly Func<TState, CancellationToken, Task> _onProjected;
    private bool _isCaughtUp;
    private TState _state;

    public ProjectionContext(
        Func<TState, CancellationToken, Task> onSubscribing,
        Func<CancellationToken, Task<TState>> onProjecting,
        Func<TState, CancellationToken, Task> onProjected)
    {
        _onSubscribing = onSubscribing;
        _onProjecting = onProjecting;
        _onProjected = onProjected;
    }

    public async Task OnSubscribing(CancellationToken cancellationToken = default)
    {
        _state = await _onProjecting(cancellationToken);
        await _onSubscribing(_state, cancellationToken);
    }

    public async Task OnCaughtUp(CancellationToken cancellationToken = default)
    {
        if (_isCaughtUp) return;
        await _onProjected(_state, cancellationToken);
        _state = default;
        _isCaughtUp = true;
    }

    public async Task OnBeforeHandleEvent(CancellationToken cancellationToken = default)
    {
        if (!_isCaughtUp) return;
        _state = await _onProjecting(cancellationToken);
    }

    public async Task OnAfterHandleEvent(CancellationToken cancellationToken = default)
    {
        if (!_isCaughtUp) return;
        await _onProjected(_state, cancellationToken);
        _state = default;
    }

    public RunProjection CreateProjectionFunc(Func<object, EventMetadata, TState, Task> eventHandler)
    {
        return (e, metadata) => eventHandler(e, metadata, _state);
    }
}