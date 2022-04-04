using System;
using System.Threading.Tasks;

namespace DomainBlocks.Projections
{
    public interface IEventPublisher<TEventBase>
    {
        Task StartAsync(Func<EventNotification<TEventBase>, Task> onEvent);
        void Stop();
    }
}