using System.Threading.Channels;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Buffers the live events that a subscription selects, for one cycle. It never blocks the feed. On a queue overflow or
/// a feed reset, it records the reason and completes its queue, and the cycle restarts once the queue is drained, which
/// can be much later.
/// </summary>
internal sealed class SubscriptionObserver<TEvent, TSourceEvent>(
    int queueCapacity,
    EventFilter liveFilter,
    ILogger? logger = null,
    string subscriptionId = "") :
    IFeedObserver<TSourceEvent>,
    IDisposable
    where TEvent : notnull
    where TSourceEvent : class, IFilterableEvent, IReadEvent<TEvent, string, StreamPosition, LogPosition>
{
    private readonly Channel<ReadEvent<TEvent, string, StreamPosition, LogPosition>> _channel =
        Channel.CreateBounded<ReadEvent<TEvent, string, StreamPosition, LogPosition>>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

    private int _stopReason;
    private bool _isStopped;

    public ChannelReader<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Reader => _channel.Reader;

    public SubscriptionStopReason StopReason => (SubscriptionStopReason)Volatile.Read(ref _stopReason);

    public ValueTask OnNextAsync(TSourceEvent item, CancellationToken cancellationToken)
    {
        // A stopped observer stays attached until the subscription replaces it, so it skips items instead of decoding
        // events that would be dropped.
        if (_isStopped || !liveFilter.Matches(item))
            return ValueTask.CompletedTask;

        ReadEvent<TEvent, string, StreamPosition, LogPosition> e;

        try
        {
            // Each store reuses one source event for every item, so the item is only valid during this call.
            e = ReadEvent.Create(item.Payload, item.Metadata, item.Context);
        }
        catch (Exception ex)
        {
            // An event that cannot be decoded fails this subscription, as it would fail a read. If the exception
            // reached the feed, the feed would detach the observer without telling the subscriber.
            _isStopped = true;
            _channel.Writer.TryComplete(ex);

            return ValueTask.CompletedTask;
        }

        if (!_channel.Writer.TryWrite(e) && Stop(SubscriptionStopReason.QueueOverflow))
            logger?.SubscriptionQueueOverflowed(subscriptionId, queueCapacity);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnResetAsync(CancellationToken cancellationToken)
    {
        if (Stop(SubscriptionStopReason.FeedReset))
            logger?.SubscriptionFeedReset(subscriptionId);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete(exception);
        return ValueTask.CompletedTask;
    }

    public void Dispose() => Stop(SubscriptionStopReason.Disposed);

    // Only the first reason is kept, and the result says whether this call set it. The reason is recorded before the
    // queue completes, so it can be read once the queue ends.
    private bool Stop(SubscriptionStopReason reason)
    {
        _isStopped = true;

        var previous = Interlocked.CompareExchange(ref _stopReason, (int)reason, (int)SubscriptionStopReason.None);

        if (previous != (int)SubscriptionStopReason.None)
            return false;

        _channel.Writer.TryComplete();

        return true;
    }
}