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
        private readonly EventStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;
        private Position _lastProcessedPosition;

        public EventStoreEventPublisher(IEventStoreConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _subscriptionDroppedHandler = new EventStoreDroppedSubscriptionHandler(Stop, ReSubscribeAfterDrop);
        }

        public Task StartAsync(Func<EventNotification<byte[]>, Task> onEvent)
        {
            _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            SubscribeToEventStore(Position.Start);

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

        private void SubscribeToEventStore(Position position)
        {
            _subscription = _connection.FilteredSubscribeToAllFrom(position,
                                                                   Filter.ExcludeSystemEvents,
                                                                   CatchUpSubscriptionFilteredSettings.Default,
                                                                   HandleEvent,
                                                                   OnLiveProcessingStarted,
                                                                   OnSubscriptionDropped,
                                                                   new UserCredentials("admin", "changeit"));
        }

        private async Task HandleEvent(EventStoreCatchUpSubscription subscription, ResolvedEvent resolvedEvent)
        {
            var notification = EventNotification.FromEvent(resolvedEvent.Event.Data,
                                                           resolvedEvent.Event.EventType,
                                                           resolvedEvent.Event.EventId);
            await _onEvent(notification);

            if (resolvedEvent.OriginalPosition.HasValue)
            {
                _lastProcessedPosition = resolvedEvent.OriginalPosition.Value;
            }
        }

        private void OnLiveProcessingStarted(EventStoreCatchUpSubscription subscription)
        {
            _onEvent(EventNotification.CaughtUp<byte[]>());
        }

        private void OnSubscriptionDropped(EventStoreCatchUpSubscription subscription, SubscriptionDropReason reason, Exception exception)
        {
            _subscriptionDroppedHandler.HandleDroppedSubscription(reason, exception);
        }

        private Task ReSubscribeAfterDrop()
        {
            SubscribeToEventStore(_lastProcessedPosition);
            return Task.CompletedTask;
        }
    }
}