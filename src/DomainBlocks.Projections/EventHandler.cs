using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections;

public delegate Task EventHandler<in TEvent, in TState>(
    IEventRecord<TEvent> eventRecord,
    TState state,
    CancellationToken cancellationToken);