using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using System;
using System.Threading.Tasks;

namespace DomainLib.Projections.EventStore
{
    public class EventStoreEventPublisher : IEventPublisher<byte[]>, IDisposable
    {
        private readonly IEventStoreConnection _connection;
        private Func<EventNotification<byte[]>, Task> _onEvent;
        private EventStoreCatchUpSubscription _subscription;
        
        public EventStoreEventPublisher(IEventStoreConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public Task StartAsync(Func<EventNotification<byte[]>, Task> onEvent)
        {
            _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            var settings = CatchUpSubscriptionFilteredSettings.Default;
            _subscription = _connection.FilteredSubscribeToAllFrom(Position.Start,
                                                                   Filter.ExcludeSystemEvents,
                                                                   settings,
                                                                   HandleEvent,
                                                                   OnLiveProcessingStarted,
                                                                   OnSubscriptionDropped,
                                                                   new UserCredentials("admin", "changeit"));

            return Task.CompletedTask;
        }

        public void Stop()
        {
            Dispose();
        }

        public void Dispose()
        {
            _subscription.Stop();
        }

        private async Task HandleEvent(EventStoreCatchUpSubscription subscription, ResolvedEvent resolvedEvent)
        {
            var notification = EventNotification.FromEvent(resolvedEvent.Event.Data,
                                                           resolvedEvent.Event.EventType,
                                                           resolvedEvent.Event.EventId);
            await _onEvent(notification);
        }

        private void OnLiveProcessingStarted(EventStoreCatchUpSubscription arg1)
        {
            _onEvent(EventNotification.CaughtUp<byte[]>());
        }

        private void OnSubscriptionDropped(EventStoreCatchUpSubscription subscription, SubscriptionDropReason reason, Exception exception)
        {
            // TODO: Need to handle this
            // https://github.com/daniel-smith/domain-lib/issues/29
        }
    }
}