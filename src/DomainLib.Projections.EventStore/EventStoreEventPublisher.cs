using System;
using System.Threading.Tasks;
using EventStore.Client;

namespace DomainLib.Projections.EventStore
{
    public class EventStoreEventPublisher : IEventPublisher<ReadOnlyMemory<byte>>, IDisposable
    {
        private readonly EventStoreClient _client;
        private Func<EventNotification<ReadOnlyMemory<byte>>, Task> _onEvent;
        private StreamSubscription _subscription;
        private readonly EventStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;
        private Position _lastProcessedPosition;

        public EventStoreEventPublisher(EventStoreClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _subscriptionDroppedHandler = new EventStoreDroppedSubscriptionHandler(Stop, ReSubscribeAfterDrop);
        }

        public async Task StartAsync(Func<EventNotification<ReadOnlyMemory<byte>>, Task> onEvent)
        {
            _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            await SubscribeToEventStore(Position.Start);
        }

        public void Stop()
        {
            Dispose();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
        }

        private async Task SubscribeToEventStore(Position position)
        {
            async Task SendEventNotification(ResolvedEvent resolvedEvent)
            {
                var notification = EventNotification.FromEvent(resolvedEvent.Event.Data,
                                                               resolvedEvent.Event.EventType,
                                                               resolvedEvent.Event.EventId.ToGuid());
                await _onEvent(notification);

                if (resolvedEvent.OriginalPosition.HasValue)
                {
                    _lastProcessedPosition = resolvedEvent.OriginalPosition.Value;
                }
            }

            var historicEvents = _client.ReadAllAsync(Direction.Forwards, position);

            await foreach (var historicEvent in historicEvents)
            {
                await SendEventNotification(historicEvent);
            }

            await _onEvent(EventNotification.CaughtUp<ReadOnlyMemory<byte>>());

            _subscription = await _client.SubscribeToAllAsync(_lastProcessedPosition,
                                                              (_, evt, _) => SendEventNotification(evt),
                                                              false,
                                                              OnSubscriptionDropped,
                                                              userCredentials: new UserCredentials("admin",
                                                                  "changeit"));
        }
        
        private void OnSubscriptionDropped(StreamSubscription subscription, SubscriptionDroppedReason reason, Exception exception)
        {
            _subscriptionDroppedHandler.HandleDroppedSubscription(reason, exception);
        }

        private async Task ReSubscribeAfterDrop()
        {
            await SubscribeToEventStore(_lastProcessedPosition);
        }
    }
}