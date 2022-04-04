using System;
using System.Threading.Tasks;
using DomainBlocks.Common;
using EventStore.Client;
using Microsoft.Extensions.Logging;
using UserCredentials = EventStore.Client.UserCredentials;

namespace DomainBlocks.Projections.EventStore
{
    public class EventStoreEventPublisher<TEventBase> : IEventPublisher<TEventBase>, IDisposable
    {
        private static readonly ILogger<EventStoreEventPublisher<TEventBase>> Log = Logger.CreateFor<EventStoreEventPublisher<TEventBase>>();
        private readonly EventStoreClient _client;
        private readonly EventStoreEventNotificationFactory _eventNotificationFactory;
        private Func<EventNotification<TEventBase>, Task> _onEvent;
        private StreamSubscription _subscription;
        private readonly EventStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;
        private Position _lastProcessedPosition;

        public EventStoreEventPublisher(EventStoreClient client, EventStoreEventNotificationFactory eventNotificationFactory)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _eventNotificationFactory = eventNotificationFactory;
            _subscriptionDroppedHandler = new EventStoreDroppedSubscriptionHandler(Stop, ReSubscribeAfterDrop);
        }

        public async Task StartAsync(Func<EventNotification<TEventBase>, Task> onEvent)
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
                await _onEvent(_eventNotificationFactory.CreateEventNotification<TEventBase>(resolvedEvent));

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

            await _onEvent(EventNotification.CaughtUp<TEventBase>());

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