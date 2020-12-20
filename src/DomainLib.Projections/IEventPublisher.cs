using System;
using System.Threading.Tasks;

namespace DomainLib.Projections
{
    public interface IEventPublisher<TEventData>
    {
        Task StartAsync(Func<EventNotification<TEventData>, Task> onEvent);
        void Stop();
    }
}