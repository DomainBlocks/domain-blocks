using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.New;

internal class ProjectionContext<TState> : IProjectionContext
{
    private readonly Func<CancellationToken, Task> _onInitializing;
    private readonly Func<CancellationToken, Task<object>> _onCatchingUp;
    private readonly Func<object, CancellationToken, Task> _onCaughtUp;
    private readonly Func<CancellationToken, Task<object>> _onEventDispatching;
    private readonly Func<object, CancellationToken, Task> _onEventHandled;
    private readonly Func<object, TState> _catchUpContextToStateSelector;
    private readonly Func<object, TState> _eventContextToStateSelector;
    private object _catchUpContext;
    private object _eventContext;
    private TState _state;

    public ProjectionContext(
        Func<CancellationToken, Task> onInitializing,
        Func<CancellationToken, Task<object>> onCatchingUp,
        Func<object, CancellationToken, Task> onCaughtUp,
        Func<CancellationToken, Task<object>> onEventDispatching,
        Func<object, CancellationToken, Task> onEventHandled,
        Func<object, TState> catchUpContextToStateSelector,
        Func<object, TState> eventContextToStateSelector)
    {
        _onInitializing = onInitializing ?? (_ => Task.CompletedTask);
        _onCatchingUp = onCatchingUp ?? (_ => null);
        _onCaughtUp = onCaughtUp ?? ((_, _) => Task.CompletedTask);
        _onEventDispatching = onEventDispatching ?? (_ => null);
        _onEventHandled = onEventHandled ?? ((_, _) => Task.CompletedTask);
        _catchUpContextToStateSelector = catchUpContextToStateSelector ?? (_ => default);
        _eventContextToStateSelector = eventContextToStateSelector ?? (_ => default);
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        await _onInitializing(cancellationToken);

        // TODO (DS): We need a proper hook for OnCatchingUp
        _catchUpContext = await _onCatchingUp(cancellationToken);
        _state = _catchUpContextToStateSelector(_catchUpContext);
    }

    public async Task OnCaughtUp(CancellationToken cancellationToken = default)
    {
        await _onCaughtUp(_catchUpContext, cancellationToken);
        _catchUpContext = null;
        _state = default;
    }

    public async Task OnEventDispatching(CancellationToken cancellationToken = default)
    {
        if (_catchUpContext != null) return;
        _eventContext = await _onEventDispatching(cancellationToken);
        _state = _eventContextToStateSelector(_catchUpContext);
    }

    public async Task OnEventHandled(CancellationToken cancellationToken = default)
    {
        if (_catchUpContext != null) return;
        await _onEventHandled(_eventContext, cancellationToken);
        _eventContext = null;
        _state = default;
    }

    public RunProjection CreateProjectionFunc(Func<object, EventMetadata, TState, Task> eventHandler)
    {
        return (e, metadata) => eventHandler(e, metadata, _state);
    }
}