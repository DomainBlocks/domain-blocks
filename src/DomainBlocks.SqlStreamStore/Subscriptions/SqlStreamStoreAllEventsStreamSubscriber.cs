using System.Runtime.CompilerServices;
using DomainBlocks.Core.Subscriptions;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using SqlStreamStoreSubscriptionDroppedReason =
    DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions.SubscriptionDroppedReason;

namespace DomainBlocks.SqlStreamStore.Subscriptions;

public sealed class SqlStreamStoreAllEventsStreamSubscriber : IEventStreamSubscriber<StreamMessage, long>
{
    private readonly IStreamStore _streamStore;
    private readonly int _readPageSize;

    public SqlStreamStoreAllEventsStreamSubscriber(IStreamStore streamStore, int readPageSize = 600)
    {
        _streamStore = streamStore;
        _readPageSize = readPageSize;
    }

    public async Task<IDisposable> Subscribe(
        long? fromPositionExclusive,
        Func<CancellationToken, Task> onCatchingUp,
        Func<StreamMessage, long, CancellationToken, Task> onEvent,
        Func<CancellationToken, Task> onLive,
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped,
        CancellationToken cancellationToken)
    {
        await onCatchingUp(cancellationToken);

        var catchUpPosition = fromPositionExclusive ?? Position.Start;
        var subscribePosition = fromPositionExclusive;

        if (catchUpPosition != Position.End)
        {
            var isFirstEventRequired = fromPositionExclusive == null;
            var skipCount = isFirstEventRequired ? 0 : 1;
            var historicEvents = ReadAllEvents(catchUpPosition, cancellationToken).Skip(skipCount);

            await foreach (var @event in historicEvents.WithCancellation(cancellationToken))
            {
                await onEvent(@event, @event.Position, cancellationToken);
                subscribePosition = @event.Position;
            }
        }

        var subscription = _streamStore.SubscribeToAll(
            subscribePosition,
            (_, e, ct) => onEvent(e, e.Position, ct),
            (_, r, ex) =>
            {
                var reason = GetSubscriptionDroppedReason(r);
                var task = Task.Run(
                    () => onSubscriptionDropped(reason, ex, cancellationToken), cancellationToken);
                task.Wait(cancellationToken);
            },
            prefetchJsonData: false);

        await onLive(cancellationToken);

        return subscription;
    }

    private async IAsyncEnumerable<StreamMessage> ReadAllEvents(
        long position,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Get the first page.
        var page = await _streamStore.ReadAllForwards(position, _readPageSize, true, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var message in page.Messages)
            {
                yield return message;
            }

            if (page.IsEnd)
            {
                break;
            }

            // Get the next page.
            page = await page.ReadNext(cancellationToken);
        }
    }

    private static SubscriptionDroppedReason GetSubscriptionDroppedReason(
        SqlStreamStoreSubscriptionDroppedReason r) => r switch
    {
        SqlStreamStoreSubscriptionDroppedReason.Disposed => SubscriptionDroppedReason.Disposed,
        SqlStreamStoreSubscriptionDroppedReason.SubscriberError => SubscriptionDroppedReason.SubscriberError,
        SqlStreamStoreSubscriptionDroppedReason.StreamStoreError => SubscriptionDroppedReason.ServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, null)
    };
}