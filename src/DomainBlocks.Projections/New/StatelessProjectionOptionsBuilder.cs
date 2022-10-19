using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class StatelessProjectionOptionsBuilder
{
    private readonly StatelessProjectionOptions _options;

    public StatelessProjectionOptionsBuilder(StatelessProjectionOptions options)
    {
        _options = options;
    }

    public void OnInitializing(Func<CancellationToken, Task> onInitializing)
    {
        _options.WithOnInitializing(onInitializing);
    }

    public void OnCatchingUp(Func<CancellationToken, Task> onCatchingUp)
    {
        _options.WithOnCatchingUp(onCatchingUp);
    }

    public void OnCaughtUp(Func<CancellationToken, Task> onCaughtUp)
    {
        _options.WithOnCaughtUp(onCaughtUp);
    }

    public void OnEventDispatching(Func<CancellationToken, Task> onEventDispatching)
    {
        _options.WithOnEventDispatching(onEventDispatching);
    }

    public void OnEventHandled(Func<CancellationToken, Task> onEventHandled)
    {
        _options.WithOnEventHandled(onEventHandled);
    }

    public void When<TEvent>(Func<TEvent, Task> eventHandler)
    {
        _options.WithDefaultEventName<TEvent>();
        _options.WithEventHandler(eventHandler);
    }
}