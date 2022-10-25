using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public interface IServiceProjectionOptions<TService> : IProjectionOptions
{
    Func<IDisposable> ResourceFactory { get; }
    Func<IDisposable, TService> ServiceFactory { get; }
    Func<TService, CancellationToken, Task> OnInitializing { get; }
    Func<TService, CancellationToken, Task> OnCatchingUp { get; }
    Func<TService, CancellationToken, Task> OnCaughtUp { get; }
    Func<TService, CancellationToken, Task> OnEventDispatching { get; }
    Func<TService, CancellationToken, Task> OnEventHandled { get; }
}