using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public interface IEventHandlerInterceptor<in TState>
{
    Task Handle(
        IEventRecord<object> eventRecord,
        TState state,
        Func<CancellationToken, Task> continuation,
        CancellationToken cancellationToken);
}