using System.Threading.Channels;
using DomainBlocks.EventStore.Filtering;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Buffers the live events that a subscription selects, for one cycle. It never blocks the tail. On a queue overflow,
/// it records the reason and completes its queue, and the cycle restarts once the queue is drained, which can be much
/// later.
/// </summary>
internal sealed class SubscriptionQueue<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos>(
    int capacity,
    Func<TSourceEvent, bool> selects,
    ILogger? logger = null,
    string subscriptionId = "") :
    ITailObserver<TSourceEvent>,
    IDisposable
    where TEvent : notnull
    where TSourceEvent : IFilterableEvent, IReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly Channel<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> _channel =
        Channel.CreateBounded<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>(
            new BoundedChannelOptions(capacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

    private int _stopReason;
    private bool _isStopped;

    public ChannelReader<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> Reader => _channel.Reader;

    public SubscriptionStopReason StopReason => (SubscriptionStopReason)Volatile.Read(ref _stopReason);

    public void OnNext(TSourceEvent sourceEvent)
    {
        // A stopped queue stays attached until the subscription replaces it, so it skips events instead of decoding
        // events that would be dropped.
        if (_isStopped || !selects(sourceEvent))
            return;

        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> readEvent;

        try
        {
            // The source event is only valid during this call.
            readEvent = ReadEvent.Create(sourceEvent.Payload, sourceEvent.Metadata, sourceEvent.Context);
        }
        catch (Exception ex)
        {
            // An event that cannot be decoded fails this subscription, as it would fail a read. If the exception
            // reached the tail, the tail would detach the queue without telling the subscriber.
            _isStopped = true;
            _channel.Writer.TryComplete(ex);

            return;
        }

        if (!_channel.Writer.TryWrite(readEvent) && Stop(SubscriptionStopReason.QueueOverflow))
            logger?.SubscriptionQueueOverflowed(subscriptionId, capacity);
    }

    public void OnError(Exception error) => _channel.Writer.TryComplete(error);

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