using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public interface IEventHandlerInterceptor
{
    Task Handle<TState>(
        IEventRecord<object> eventRecord,
        TState state,
        Func<CancellationToken, Task> continuation,
        CancellationToken cancellationToken);
}