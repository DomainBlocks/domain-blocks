using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections;

public interface IEventPublisher<TEventData>
{
    Task StartAsync(
        Func<EventNotification<TEventData>, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default);
    void Stop();
}
