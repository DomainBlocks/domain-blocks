using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New.Internal;

internal interface IEventHandlerInvoker<in TState>
{
    Task Invoke(IEventRecord<object> eventRecord, TState state, CancellationToken cancellationToken);
}