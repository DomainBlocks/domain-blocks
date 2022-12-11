using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.Internal;

internal interface IEventHandlerInvoker<in TState>
{
    Task Invoke(IEventRecord eventRecord, TState state, CancellationToken cancellationToken);
}