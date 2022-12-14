using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections;

public interface IEventPublisher<TEventData>
{
    Task StartAsync(
        Func<EventNotification<TEventData>, CancellationToken, Task> onEvent,
        // TODO (DS): Think about the design here, as a starting position doesn't make sense for all types of
        // subscriptions. This can be considered when we start looking at process managers.
        IStreamPosition position = null,
        CancellationToken cancellationToken = default);

    void Stop();
}