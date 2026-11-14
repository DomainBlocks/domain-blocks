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
/// <remarks>
/// The observer applies the subscription's retry limits to the feed's failures while the subscription depends on the
/// feed: until the feed first connects, and once the subscription is live. When a limit is reached, the queue completes
/// with the failure's error. In between, the subscription is catching up, which the feed doesn't hold back, and the
/// feed may recover before the replay ends, so its failures don't count.
/// </remarks>
internal sealed class SubscriptionObserver<TEvent, TSourceEvent>(
    int queueCapacity,
    EventFilter liveFilter,
    ILogger? logger = null,
    string subscriptionId = "",
    int maxRetryAttempts = int.MaxValue,
    TimeSpan? retryTimeout = null) :
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

    private readonly FeedRetryLimit _connectionLimit = new(maxRetryAttempts, retryTimeout ?? Timeout.InfiniteTimeSpan);
    private FeedRetryLimit? _liveLimit;
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

    public ValueTask OnFailureAsync(FeedFailure failure, CancellationToken cancellationToken)
    {
        var limit = Volatile.Read(ref _liveLimit) ?? (failure.HasConnected ? null : _connectionLimit);

        if (limit is null || !limit.IsReachedBy(failure))
            return ValueTask.CompletedTask;

        _isStopped = true;

        if (_channel.Writer.TryComplete(failure.Error))
            logger?.SubscriptionRetryLimitReached(subscriptionId);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete(exception);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Records that the subscription has caught up, so the feed's failures from now on count towards its retry limits.
    /// </summary>
    public void OnCaughtUp()
    {
        Volatile.Write(ref _liveLimit, new FeedRetryLimit(maxRetryAttempts, retryTimeout ?? Timeout.InfiniteTimeSpan));
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