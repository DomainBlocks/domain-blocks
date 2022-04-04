using System;
using System.Threading;
using System.Threading.Tasks;
using SqlStreamStore;
using SqlStreamStore.Streams;
using SqlStreamStore.Subscriptions;

namespace DomainBlocks.Projections.SqlStreamStore
{
    public class SqlStreamStoreEventPublisher<TEventBase> : IEventPublisher<TEventBase>, IDisposable
    {
        private readonly IStreamStore _streamStore;
        private readonly SqlStreamStoreEventNotificationFactory _notificationFactory;
        private Func<EventNotification<TEventBase>, Task> _onEvent;
        private IAllStreamSubscription _subscription;
        private readonly SqlStreamStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;
        private long? _lastProcessedPosition;

        public SqlStreamStoreEventPublisher(IStreamStore streamStore, 
                                            SqlStreamStoreEventNotificationFactory notificationFactory)
        {
            _streamStore = streamStore;
            _notificationFactory = notificationFactory;
            _subscriptionDroppedHandler = new SqlStreamStoreDroppedSubscriptionHandler(Stop, ReSubscribeAfterDrop);
        }

        public async Task StartAsync(Func<EventNotification<TEventBase>, Task> onEvent)
        {
            _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            await SubscribeToStore(null).ConfigureAwait(false);
        }

        public void Stop()
        {
            Dispose();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
        }

        private async Task SubscribeToStore(long? subscribePosition)
        {
            _subscription = _streamStore.SubscribeToAll(subscribePosition, StreamMessageReceived, SubscriptionDropped,
                                                        caughtUp =>
                                                        {
                                                            if (caughtUp)
                                                            {
                                                                // SqlStreamStore doesn't provide us with an async delegate.
                                                                // To avoid ordering issues, we want to ensure no other
                                                                // event processing is done until our caught up
                                                                // notification is published , so wait on the task
                                                                _onEvent(EventNotification.CaughtUp<TEventBase>()).Wait();
                                                            }
                                                        });
            // TODO: allow this to be configured
            _subscription.MaxCountPerRead = 1000;
            await _subscription.Started.ConfigureAwait(false);
        }

        private async Task StreamMessageReceived(IAllStreamSubscription subscription, StreamMessage streamMessage, CancellationToken cancellationToken)
        {
            await SendEventNotification(streamMessage);
        }

        private void SubscriptionDropped(IAllStreamSubscription subscription, SubscriptionDroppedReason reason, Exception exception)
        {
            _subscriptionDroppedHandler.HandleDroppedSubscription(reason, exception);
        }

        private async Task SendEventNotification(StreamMessage message)
        {
            var jsonData = await message.GetJsonData().ConfigureAwait(false);
            var wrapper = new StreamMessageWrapper(message, jsonData);
            var notification = _notificationFactory.CreateEventNotification<TEventBase>(wrapper);

            await _onEvent(notification).ConfigureAwait(false);
            _lastProcessedPosition = message.Position;
        }

        private async Task ReSubscribeAfterDrop()
        {
            await SubscribeToStore(_lastProcessedPosition).ConfigureAwait(false);
        }
    }
}
