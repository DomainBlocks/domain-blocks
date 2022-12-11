using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections;

public interface IEventHandlerInterceptor<in TState>
{
    Task Handle(
        IEventRecord eventRecord,
        TState state,
        Func<CancellationToken, Task> continuation,
        CancellationToken cancellationToken);
}