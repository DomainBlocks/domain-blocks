using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;

namespace DomainBlocks.Projections;

public interface IEventPublisher<TEventData>
{
    Task StartAsync(
        Func<EventNotification<TEventData>, CancellationToken, Task> onEvent,
        // TODO: Think of the design here, as a starting position doesn't make sense for all types of subscriptions.
        IStreamPosition position = null,
        CancellationToken cancellationToken = default);

    void Stop();
}