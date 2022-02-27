using System;
using System.Threading;
using System.Threading.Tasks;
using SqlStreamStore;
using SqlStreamStore.Streams;
using SqlStreamStore.Subscriptions;

namespace DomainBlocks.Projections.SqlStreamStore
{
    public struct SqlStreamStoreRawEvent
    {
        public SqlStreamStoreRawEvent(string eventData, string metadata)
        {
            EventData = eventData;
            Metadata = metadata;
        }

        public string EventData { get; }
        public string Metadata { get; }

        public static async Task<SqlStreamStoreRawEvent> FromStreamMessage(StreamMessage message)
        {
            var eventData = await message.GetJsonData();
            return new SqlStreamStoreRawEvent(eventData, message.JsonMetadata);
        }
    }

    public class SqlStreamStoreEventPublisher : IEventPublisher<SqlStreamStoreRawEvent>, IDisposable
    {
        private readonly IStreamStore _streamStore;
        private Func<EventNotification<SqlStreamStoreRawEvent>, Task> _onEvent;
        private IAllStreamSubscription _subscription;
        private readonly SqlStreamStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;
        private long? _lastProcessedPosition;

        public SqlStreamStoreEventPublisher(IStreamStore streamStore)
        {
            _streamStore = streamStore;
            _subscriptionDroppedHandler = new SqlStreamStoreDroppedSubscriptionHandler(Stop, ReSubscribeAfterDrop);
        }

        public async Task StartAsync(Func<EventNotification<SqlStreamStoreRawEvent>, Task> onEvent)
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
                                                                _onEvent(EventNotification.CaughtUp<SqlStreamStoreRawEvent>()).Wait();
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

        async Task SendEventNotification(StreamMessage message)
        {
            var notification = EventNotification.FromEvent(await SqlStreamStoreRawEvent.FromStreamMessage(message),
                                                           message.Type,
                                                           message.MessageId);
            await _onEvent(notification).ConfigureAwait(false);
            _lastProcessedPosition = message.Position;
        }

        private async Task ReSubscribeAfterDrop()
        {
            await SubscribeToStore(_lastProcessedPosition).ConfigureAwait(false);
        }
    }
}
