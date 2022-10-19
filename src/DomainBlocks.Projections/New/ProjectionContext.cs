using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

internal class ProjectionContext : IProjectionContext
{
    private readonly Func<CancellationToken, Task> _onInitializing;
    private readonly Func<CancellationToken, Task> _onCatchingUp;
    private readonly Func<CancellationToken, Task> _onCaughtUp;
    private readonly Func<CancellationToken, Task> _onEventDispatching;
    private readonly Func<CancellationToken, Task> _onEventHandled;

    public ProjectionContext(
        Func<CancellationToken, Task> onInitializing,
        Func<CancellationToken, Task> onCatchingUp,
        Func<CancellationToken, Task> onCaughtUp,
        Func<CancellationToken, Task> onEventDispatching,
        Func<CancellationToken, Task> onEventHandled)
    {
        _onInitializing = onInitializing ?? (_ => Task.CompletedTask);
        _onCatchingUp = onCatchingUp ?? (_ => Task.CompletedTask);
        _onCaughtUp = onCaughtUp ?? (_ => Task.CompletedTask);
        _onEventDispatching = onEventDispatching ?? (_ => Task.CompletedTask);
        _onEventHandled = onEventHandled ?? (_ => Task.CompletedTask);
    }

    public async Task OnInitializing(CancellationToken cancellationToken = default)
    {
        await _onInitializing(cancellationToken);

        // TODO (DS): We need a proper OnCatchingUp hook.
        await _onCatchingUp(cancellationToken);
    }

    public Task OnCaughtUp(CancellationToken cancellationToken = default) => _onCaughtUp(cancellationToken);

    public Task OnEventDispatching(CancellationToken cancellationToken = default) =>
        _onEventDispatching(cancellationToken);

    public Task OnEventHandled(CancellationToken cancellationToken = default) => _onEventHandled(cancellationToken);
}