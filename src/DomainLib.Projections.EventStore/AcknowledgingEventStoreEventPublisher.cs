using System;
using System.Threading.Tasks;

namespace DomainLib.Projections.EventStore
{
    public class AcknowledgingEventStoreEventPublisher<TEventBase> : IEventPublisher<TEventBase>
    {

        public Task StartAsync(Func<EventNotification<TEventBase>, Task> onEvent)
        {
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }
    }
}
