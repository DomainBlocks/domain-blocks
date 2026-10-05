using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A catch-up-then-live subscription, ported from the MongoDB implementation.
/// </summary>
/// <remarks>
/// <para>
/// A subscription follows one sequence of positions: those of the log, or those of a stream. Its resume position, its
/// high-water mark, and the position of each of its events are all positions in that sequence.
/// </para>
/// <para>
/// Each cycle attaches an observer to the live feed first, then reads the high-water mark (the last position of the
/// sequence), replays everything from the resume position up to it, emits
/// <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}.CaughtUp"/>,
/// and finally drains the live rows, skipping any that the replay has covered. Because positions are assigned in
/// commit order without gaps, the replay and the live rows tile exactly: nothing is skipped and nothing is repeated.
/// </para>
/// <para>
/// A cycle is restarted from the resume position, after emitting
/// <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}.FellBehind"/>, when the subscriber's queue
/// overflows or when the feed has been re-established and may have missed rows. A restart signaled while the cycle is
/// still replaying takes effect once the replay has reached the high-water mark. The replay is never abandoned
/// part-way, as the next cycle would have to start it again. Falling behind is only reported to a subscriber that has
/// been told it caught up, so the two messages alternate, starting with
/// <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}.CaughtUp"/>.
/// </para>
/// <para>
/// A subscription's filter selects the events of the replay in the query that reads them, and the live rows in memory.
/// The live feed hands every subscription each row of the log undecoded. A subscription takes the event of a row only
/// if it selects the row. It neither decodes nor queues the events it does not select, and an event that cannot be
/// decoded fails only the subscriptions that select it.
/// </para>
/// <para>
/// The resume position only moves forward. A replay that reaches its high-water mark moves it there, whether or not it
/// delivered anything, as a filter can leave long stretches of the sequence with nothing to deliver and the next cycle
/// should not read them again. Delivering a live event moves it to that event.
/// </para>
/// <para>
/// A subscription from the end is pinned to the last position at the time it starts. A cycle that restarts before
/// delivering an event then resumes from that position, rather than from a later end that would skip the events
/// appended in between.
/// </para>
/// </remarks>
internal sealed class SubscriptionAsyncEnumerable<TEvent, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly EventLogReader<TEvent> _reader;
    private readonly IRefCountedEventLogFeed<EventLogRow<TEvent>> _feed;
    private readonly CatchUpReader _catchUpReader;
    private readonly EndPositionReader _endPositionReader;
    private readonly EventFilter _liveFilter;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public SubscriptionAsyncEnumerable(
        EventLogReader<TEvent> reader,
        IRefCountedEventLogFeed<EventLogRow<TEvent>> feed,
        CatchUpReader catchUpReader,
        EndPositionReader endPositionReader,
        EventFilter liveFilter,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        ILogger? logger)
    {
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _catchUpReader = catchUpReader;
        _endPositionReader = endPositionReader;
        _liveFilter = liveFilter;
        _positionSelector = positionSelector;
        _reader = reader;
        _feed = feed;
        _logger = logger;

        _correlationId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();
    }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        Observer? observer = null;
        IAsyncDisposable? attachment = null;

        try
        {
            _logger?.SubscriptionStarted(_correlationId);

            var resumePosition = new ResumePosition(_origin switch
            {
                SubscriptionOrigin<TPos>.After after => after.Position,
                SubscriptionOrigin<TPos>.End => await PinEndAsync(cancellationToken).ConfigureAwait(false),
                _ => null
            });

            var isCaughtUp = false;

            while (true)
            {
                // Every cycle but the first is a restart. It can come long after the observer logged that one was
                // pending, so it is logged when it takes place.
                if (observer is not null)
                {
                    if (resumePosition.After is { } after)
                        _logger?.SubscriptionRestarting(_correlationId, after.Value);
                    else
                        _logger?.SubscriptionRestartingFromStart(_correlationId);
                }

                // Attach the new observer before detaching the old one, so that the ref count never drops to zero
                // between cycles and the feed's replication session survives a restart.
                var nextObserver = new Observer(_options.QueueCapacity, _liveFilter, _logger, _correlationId);
                var nextAttachment = await AttachObserverAsync(nextObserver, cancellationToken).ConfigureAwait(false);

                if (attachment is not null)
                    await attachment.DisposeAsync().ConfigureAwait(false);

                observer?.Dispose();
                observer = nextObserver;
                attachment = nextAttachment;

                var enumerator = ReadAllAsync(resumePosition, observer, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                await using (enumerator.ConfigureAwait(false))
                {
                    while (await MoveNextAsync(enumerator, cancellationToken).ConfigureAwait(false))
                    {
                        var message = enumerator.Current;
                        yield return message;

                        if (message.IsCaughtUp)
                            isCaughtUp = true;
                    }
                }

                // The observer logged why it stopped when it did, which can be long before the cycle ends, and the next
                // cycle logs the restart. A subscription that had not caught up has not fallen behind: its replay has
                // already run to the high-water mark, and the next cycle carries on from there.
                switch (observer.StopReason)
                {
                    case StopReason.QueueOverflow when isCaughtUp:
                        _logger?.SubscriptionFellBehind(_correlationId);
                        break;

                    case StopReason.QueueOverflow or StopReason.FeedReset:
                        break;

                    default:
                        yield break;
                }

                // FellBehind tells a subscriber that it is no longer caught up, so it is only reported to one that has
                // been told it caught up. A restart before then, or a second restart before it has caught up again,
                // e.g., when the live feed is still pumping a large transaction into a small queue, only makes the
                // catch-up longer. Every FellBehind is therefore answered by exactly one CaughtUp.
                if (isCaughtUp)
                {
                    isCaughtUp = false;
                    yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.FellBehind;
                }
            }
        }
        finally
        {
            if (attachment is not null)
                await attachment.DisposeAsync().ConfigureAwait(false);

            observer?.Dispose();

            _logger?.SubscriptionStopped(_correlationId);
            CorrelationId.Release(_correlationId);
        }
    }

    private async ValueTask<bool> MoveNextAsync(
        IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> enumerator,
        CancellationToken cancellationToken)
    {
        try
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.SubscriptionCanceled(_correlationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.SubscriptionFailed(ex, _correlationId);
            throw;
        }
    }

    // The position that a subscription from the end resumes after: the last of the sequence at the time it starts, or
    // none if the sequence is empty, in which case it resumes from the start.
    private async Task<TPos?> PinEndAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _endPositionReader(_reader, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.SubscriptionCanceled(_correlationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.SubscriptionFailed(ex, _correlationId);
            throw;
        }
    }

    private async Task<IAsyncDisposable> AttachObserverAsync(Observer observer, CancellationToken cancellationToken)
    {
        try
        {
            return await _feed.AttachAsync(observer, _correlationId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            observer.Dispose();
            _logger?.SubscriptionCanceled(_correlationId);
            throw;
        }
        catch (Exception ex)
        {
            observer.Dispose();
            _logger?.SubscriptionFailed(ex, _correlationId);
            throw;
        }
    }

    private async IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> ReadAllAsync(
        ResumePosition resumePosition,
        Observer observer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var highWaterMark = await _endPositionReader(_reader, cancellationToken).ConfigureAwait(false);

        _logger?.CatchUpBoundary(_correlationId, highWaterMark?.Value);

        if (highWaterMark is { } mark)
        {
            var afterExclusive = resumePosition.After is { } after ? checked((long)after.Value) : -1;
            var events = _catchUpReader(_reader, afterExclusive, checked((long)mark.Value), cancellationToken);

            await foreach (var e in events.ConfigureAwait(false))
                yield return SubscriptionMessage.Event(e);

            // A replay is never abandoned part-way, so the resume position moves once for the whole of it. It has
            // covered the sequence up to the mark, whether or not it delivered anything on the way.
            resumePosition.AdvanceTo(mark);
        }

        // A restart signaled during the replay takes effect only now that the replay is complete. Cancelling the replay
        // instead would throw away the part of the log it had already read through without delivering anything, and a
        // replay that takes longer than the queue takes to overflow would never finish.
        if (observer.StopReason != StopReason.None)
            yield break;

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

        // The observer's queue ends, once what it holds has been read, when the observer has stopped.
        await foreach (var e in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var position = _positionSelector(e.Context);

            // The replay has covered it.
            if (resumePosition.Covers(position))
                continue;

            yield return SubscriptionMessage.Event(e);

            // The subscriber has taken the event and come back for more.
            resumePosition.AdvanceTo(position);
        }
    }

    /// <summary>
    /// Reads the events of the sequence that the subscription follows, after one of its positions and up to another,
    /// inclusive.
    /// </summary>
    public delegate IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> CatchUpReader(
        EventLogReader<TEvent> reader,
        long afterExclusive,
        long highWaterMark,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the last position of the sequence that the subscription follows, or <see langword="null"/> if the
    /// sequence is empty.
    /// </summary>
    public delegate Task<TPos?> EndPositionReader(EventLogReader<TEvent> reader, CancellationToken cancellationToken);

    /// <summary>
    /// The position that a subscription resumes after: the furthest that it has delivered or that a replay has covered.
    /// It only moves forward.
    /// </summary>
    private sealed class ResumePosition(TPos? after)
    {
        /// <summary>
        /// The position to resume after, or <see langword="null"/> to resume from the start.
        /// </summary>
        public TPos? After { get; private set; } = after;

        public bool Covers(TPos position) => After is { } after && position.Value <= after.Value;

        public void AdvanceTo(TPos position)
        {
            if (!Covers(position))
                After = position;
        }
    }

    /// <summary>
    /// Why an observer has stopped taking rows, if it has.
    /// </summary>
    internal enum StopReason
    {
        None,
        QueueOverflow,
        FeedReset,
        Disposed
    }

    /// <summary>
    /// Buffers the live events that a subscription selects, for one subscription cycle. Never blocks the feed: on an
    /// overflow, as on a feed reset, it records the reason and completes its queue. The cycle ends once it has read
    /// what the queue holds, and a new one starts from the resume position. Nothing is interrupted to bring that about,
    /// so the restart can come much later than its cause, which is logged when it happens.
    /// </summary>
    internal sealed class Observer(
        int queueCapacity,
        EventFilter liveFilter,
        ILogger? logger = null,
        string subscriptionId = "") :
        IEventLogObserver<EventLogRow<TEvent>>,
        IDisposable
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

        public StopReason StopReason => (StopReason)Volatile.Read(ref _stopReason);

        public ValueTask OnNextAsync(EventLogRow<TEvent> row, CancellationToken cancellationToken)
        {
            // An observer that has failed or signaled a restart takes nothing more. It stays attached until the
            // subscription replaces it, and a row that it selected would be decoded only to be dropped.
            if (_isStopped || !liveFilter.Matches(row))
                return ValueTask.CompletedTask;

            ReadEvent<TEvent, string, StreamPosition, LogPosition> e;

            try
            {
                // The row is only valid during this call, so the event is taken from it now.
                e = row.DecodedEvent;
            }
            catch (Exception ex)
            {
                // An event that cannot be decoded fails this subscription, as it would a read. Thrown to the feed, it
                // would detach the observer without telling the subscriber.
                _isStopped = true;
                _channel.Writer.TryComplete(ex);

                return ValueTask.CompletedTask;
            }

            if (!_channel.Writer.TryWrite(e) && Stop(StopReason.QueueOverflow))
                logger?.SubscriptionQueueOverflowed(subscriptionId, queueCapacity);

            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken)
        {
            if (Stop(StopReason.FeedReset))
                logger?.SubscriptionFeedReset(subscriptionId);

            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete(exception);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Stop(StopReason.Disposed);

        // The first reason given is the one that stands, and the result says whether this one is it. The reason is
        // recorded before the queue is completed, so that it is there to read by the time the queue ends.
        private bool Stop(StopReason reason)
        {
            _isStopped = true;

            var previous = Interlocked.CompareExchange(ref _stopReason, (int)reason, (int)StopReason.None);

            if (previous != (int)StopReason.None)
                return false;

            _channel.Writer.TryComplete();

            return true;
        }
    }
}