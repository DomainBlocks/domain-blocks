using System;
using System.Threading;
using System.Threading.Tasks;
using SqlStreamStore;
using SqlStreamStore.Streams;
using SqlStreamStore.Subscriptions;

namespace DomainBlocks.Projections.SqlStreamStore;

public class SqlStreamStoreEventPublisher : IEventPublisher<StreamMessageWrapper>, IDisposable
{
    private readonly IStreamStore _streamStore;
    private Func<EventNotification<StreamMessageWrapper>, CancellationToken, Task> _onEvent;
    private IAllStreamSubscription _subscription;
    private readonly SqlStreamStoreDroppedSubscriptionHandler _subscriptionDroppedHandler;
    private long? _lastProcessedPosition;

    public SqlStreamStoreEventPublisher(IStreamStore streamStore)
    {
        _streamStore = streamStore;
        _subscriptionDroppedHandler = new SqlStreamStoreDroppedSubscriptionHandler(Stop, ReSubscribeAfterDrop);
    }

    public async Task StartAsync(
        Func<EventNotification<StreamMessageWrapper>, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
        await SubscribeToStore(null, cancellationToken).ConfigureAwait(false);
    }

    public void Stop()
    {
        Dispose();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    private async Task SubscribeToStore(long? subscribePosition, CancellationToken cancellationToken = default)
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
                    _onEvent(EventNotification.CaughtUp<StreamMessageWrapper>(), cancellationToken)
                        .Wait(cancellationToken);
                }
            });
        // TODO: allow this to be configured
        _subscription.MaxCountPerRead = 1000;
        await _subscription.Started.ConfigureAwait(false);
    }

    private async Task StreamMessageReceived(
        IAllStreamSubscription subscription,
        StreamMessage streamMessage,
        CancellationToken cancellationToken = default)
    {
        await SendEventNotification(streamMessage, cancellationToken: cancellationToken);
    }

    private void SubscriptionDropped(IAllStreamSubscription subscription, SubscriptionDroppedReason reason, Exception exception)
    {
        _subscriptionDroppedHandler.HandleDroppedSubscription(reason, exception);
    }

    async Task SendEventNotification(StreamMessage message, CancellationToken cancellationToken = default)
    {
        var jsonData = await message.GetJsonData(cancellationToken).ConfigureAwait(false);
        var wrapper = new StreamMessageWrapper(message, jsonData);
        var notification = EventNotification.FromEvent(wrapper,
            wrapper.Type,
            wrapper.MessageId);

        await _onEvent(notification, cancellationToken).ConfigureAwait(false);
        _lastProcessedPosition = message.Position;
    }

    private async Task ReSubscribeAfterDrop()
    {
        await SubscribeToStore(_lastProcessedPosition).ConfigureAwait(false);
    }
}