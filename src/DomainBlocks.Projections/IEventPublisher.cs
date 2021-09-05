using System;
using System.Threading.Tasks;

namespace DomainBlocks.Projections
{
    public interface IEventPublisher<TEventData>
    {
        Task StartAsync(Func<EventNotification<TEventData>, Task> onEvent);
        void Stop();
    }
}