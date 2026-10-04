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
/// Each cycle attaches an observer to the live feed first, then reads the high-water mark (the highest committed
/// position), replays everything from the resume position up to it, emits
/// <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}.CaughtUp"/>,
/// and finally drains the live rows, skipping any at or below the high-water mark. Because positions are assigned in
/// commit order without gaps, the replay and the live rows tile exactly: nothing is skipped and nothing is repeated.
/// </para>
/// <para>
/// A cycle is restarted from the last delivered position, after emitting
/// <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}.FellBehind"/>, when the subscriber's queue
/// overflows or when the feed has been re-established and may have missed rows. A restart that is signalled while the
/// cycle is still replaying takes effect once the replay has reached the high-water mark. The replay is never abandoned
/// part-way, as the next cycle would have to start it again.
/// </para>
/// <para>
/// The live feed hands every subscription each row of the log undecoded. A subscription takes the event of a row only
/// if it selects the row, so it neither decodes nor queues the events it does not select, and an event that cannot be
/// decoded fails only the subscriptions that select it.
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

            var resumeOrigin = _origin is SubscriptionOrigin<TPos>.End
                ? await PinEndAsync(cancellationToken).ConfigureAwait(false)
                : _origin;

            var fellBehindPending = false;

            while (true)
            {
                // Attach the new observer before detaching the old one, so that the ref count never drops to zero
                // between cycles and the feed's replication session survives a restart.
                var nextObserver = new Observer(_options.QueueCapacity, _liveFilter);
                var nextAttachment = await AttachObserverAsync(nextObserver, cancellationToken).ConfigureAwait(false);

                if (attachment is not null)
                    await attachment.DisposeAsync().ConfigureAwait(false);

                observer?.Dispose();
                observer = nextObserver;
                attachment = nextAttachment;

                var enumerator = ReadAllAsync(resumeOrigin, observer, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                await using (enumerator.ConfigureAwait(false))
                {
                    while (await MoveNextAsync(enumerator, observer.RestartToken, cancellationToken)
                               .ConfigureAwait(false))
                    {
                        var message = enumerator.Current;
                        yield return message;

                        if (message.IsCaughtUp)
                            fellBehindPending = false;
                        else if (message.Event is { } e)
                            resumeOrigin = SubscriptionOrigin.After(_positionSelector(e.Context));
                    }
                }

                switch (observer.RestartReason)
                {
                    case RestartReason.QueueOverflow:
                        _logger?.SubscriptionFellBehind(_correlationId);
                        break;

                    case RestartReason.FeedReset:
                        _logger?.SubscriptionFeedReset(_correlationId);
                        break;

                    default:
                        yield break;
                }

                // A cycle can restart again before it has caught up, e.g. when the live feed is still pumping a large
                // transaction into a small queue. Every FellBehind is answered by exactly one CaughtUp, so a restart
                // that has not yet been caught up on is not reported twice.
                if (!fellBehindPending)
                {
                    fellBehindPending = true;
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
        CancellationToken restartToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (restartToken.IsCancellationRequested)
        {
            return false;
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

    private async Task<SubscriptionOrigin<TPos>> PinEndAsync(CancellationToken cancellationToken)
    {
        try
        {
            var endPosition = await _endPositionReader(_reader, cancellationToken).ConfigureAwait(false);

            return endPosition is { } position ? SubscriptionOrigin.After(position) : SubscriptionOrigin.Start;
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
        SubscriptionOrigin<TPos> resumeOrigin,
        Observer observer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var highWaterMark = await _reader.GetMaxPositionAsync(cancellationToken).ConfigureAwait(false);

        _logger?.CatchUpBoundary(_correlationId, highWaterMark);

        if (highWaterMark is not null)
        {
            var afterExclusive = resumeOrigin is SubscriptionOrigin<TPos>.After after
                ? checked((long)after.Position.Value)
                : -1;

            var events = _catchUpReader(_reader, afterExclusive, highWaterMark.Value, cancellationToken);

            await foreach (var e in events.ConfigureAwait(false))
                yield return SubscriptionMessage.Event(e);
        }

        // A restart that was signalled during the replay takes effect only now that the replay is complete. Cancelling
        // the replay instead would throw away the part of the log it had already read through without delivering
        // anything, and a replay that takes longer than the queue takes to overflow would never finish.
        if (observer.RestartToken.IsCancellationRequested)
            yield break;

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

        await foreach (var e in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Catching up has delivered it.
            if ((long)e.Context.LogPosition.Value <= highWaterMark)
                continue;

            yield return SubscriptionMessage.Event(e);
        }
    }

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

    internal enum RestartReason
    {
        QueueOverflow,
        FeedReset,
        Disposed
    }

    /// <summary>
    /// Buffers the live events that a subscription selects, for one subscription cycle. Never blocks the feed: an
    /// overflow, like a feed reset, cancels the restart token and completes the channel so that the cycle ends and a new
    /// one starts from the last position.
    /// </summary>
    internal sealed class Observer(int queueCapacity, EventFilter liveFilter) :
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

        private readonly CancellationTokenSource _restartCts = new();
        private int _restartReason;
        private bool _hasFailed;

        public ChannelReader<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Reader => _channel.Reader;

        public CancellationToken RestartToken => _restartCts.Token;

        public RestartReason RestartReason => (RestartReason)Volatile.Read(ref _restartReason);

        public ValueTask OnNextAsync(EventLogRow<TEvent> row, CancellationToken cancellationToken)
        {
            // A subscription that has failed is waiting to say so. A row that it could not queue would be taken for
            // falling behind.
            if (_hasFailed || !liveFilter.Matches(row))
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
                _hasFailed = true;
                _channel.Writer.TryComplete(ex);

                return ValueTask.CompletedTask;
            }

            if (!_channel.Writer.TryWrite(e))
                Restart(RestartReason.QueueOverflow);

            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken)
        {
            Restart(RestartReason.FeedReset);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete(exception);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _restartReason, (int)RestartReason.Disposed, 0) == 0)
                _channel.Writer.TryComplete();

            _restartCts.Dispose();
        }

        private void Restart(RestartReason reason)
        {
            if (Interlocked.CompareExchange(ref _restartReason, (int)reason, 0) != 0)
                return;

            _restartCts.Cancel();
            _channel.Writer.TryComplete(new OperationCanceledException(_restartCts.Token));
        }
    }
}