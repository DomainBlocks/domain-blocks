using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public interface IServiceProjectionOptionsBuilder<out TService>
{
    void OnInitializing(Func<TService, CancellationToken, Task> onInitializing);
    void OnCatchingUp(Func<TService, CancellationToken, Task> onCatchingUp);
    void OnCaughtUp(Func<TService, CancellationToken, Task> onCaughtUp);
    void OnEventDispatching(Func<TService, CancellationToken, Task> onEventDispatching);
    void OnEventHandled(Func<TService, CancellationToken, Task> onEventHandled);
    void When<TEvent>(Func<TEvent, TService, Task> eventHandler);
    void When<TEvent>(Action<TEvent, TService> eventHandler);
}