using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ServiceProjectionOptionsBuilder<TResource, TService> : IServiceProjectionOptionsBuilder<TService> where TResource : IDisposable
{
    public ServiceProjectionOptionsBuilder(ServiceProjectionOptions<TResource, TService> options)
    {
        Options = options;
    }

    public ServiceProjectionOptions<TResource, TService> Options { get; private set; }

    public void OnInitializing(Func<TService, CancellationToken, Task> onInitializing)
    {
        Options = Options.WithOnInitializing(onInitializing);
    }

    public void OnCatchingUp(Func<TService, CancellationToken, Task> onCatchingUp)
    {
        Options = Options.WithOnCatchingUp(onCatchingUp);
    }

    public void OnCaughtUp(Func<TService, CancellationToken, Task> onCaughtUp)
    {
        Options = Options.WithOnCaughtUp(onCaughtUp);
    }

    public void OnEventDispatching(Func<TService, CancellationToken, Task> onEventDispatching)
    {
        Options = Options.WithOnEventDispatching(onEventDispatching);
    }

    public void OnEventHandled(Func<TService, CancellationToken, Task> onEventHandled)
    {
        Options = Options.WithOnEventHandled(onEventHandled);
    }

    public void When<TEvent>(Func<TEvent, TService, Task> eventHandler)
    {
        Options = Options.WithEventHandler(eventHandler);
    }

    public void When<TEvent>(Action<TEvent, TService> eventHandler)
    {
        Options = Options.WithEventHandler<TEvent>((e, dbContext) =>
        {
            eventHandler(e, dbContext);
            return Task.CompletedTask;
        });
    }
}