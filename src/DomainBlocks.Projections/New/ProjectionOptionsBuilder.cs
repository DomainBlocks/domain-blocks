using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ProjectionOptionsBuilder
{
    public ProjectionOptions Options { get; private set; } = new();

    public void OnInitializing(Func<CancellationToken, Task> onInitializing)
    {
        Options = Options.WithOnInitializing(onInitializing);
    }

    public void OnCatchingUp(Func<CancellationToken, Task> onCatchingUp)
    {
        Options = Options.WithOnCatchingUp(onCatchingUp);
    }

    public void OnCaughtUp(Func<CancellationToken, Task> onCaughtUp)
    {
        Options = Options.WithOnCaughtUp(onCaughtUp);
    }

    public void OnEventDispatching(Func<CancellationToken, Task> onEventDispatching)
    {
        Options = Options.WithOnEventDispatching(onEventDispatching);
    }

    public void OnEventHandled(Func<CancellationToken, Task> onEventHandled)
    {
        Options = Options.WithOnEventHandled(onEventHandled);
    }

    public void When<TEvent>(Func<TEvent, CancellationToken, Task> eventHandler)
    {
        Options = Options.WithEventHandler(eventHandler);
    }
}