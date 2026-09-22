using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
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
/// overflows or when the feed has been re-established and may have missed rows.
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
    private readonly CatchUpCheckpoint _catchUpCheckpoint;
    private readonly string? _streamId;
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
        CatchUpCheckpoint catchUpCheckpoint,
        string? streamId,
        EventFilter liveFilter,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        ILogger? logger)
    {
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _catchUpReader = catchUpReader;
        _catchUpCheckpoint = catchUpCheckpoint;
        _streamId = streamId;
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

            var resumeOrigin = _origin;
            var fellBehindPending = false;

            while (true)
            {
                // Attach the new observer before detaching the old one, so that the ref count never drops to zero
                // between cycles and the feed's replication session survives a restart.
                var nextObserver = new Observer(
                    _options.QueueCapacity,
                    _streamId,
                    _liveFilter,
                    _options.CheckpointInterval);

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
                        else if (CheckpointOf(message) is { } checkpoint)
                            resumeOrigin = SubscriptionOrigin.After(checkpoint);
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
        long? highWaterMark;

        // Where the subscriber has got to: what it asked to resume after, then the last event or checkpoint it is given.
        TPos? last = resumeOrigin is SubscriptionOrigin<TPos>.After resumedAfter ? resumedAfter.Position : null;

        using (var catchUpCts = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken,
                   observer.RestartToken))
        {
            highWaterMark = await _reader.GetMaxPositionAsync(catchUpCts.Token).ConfigureAwait(false);

            _logger?.CatchUpBoundary(_correlationId, highWaterMark);

            if (highWaterMark is not null && resumeOrigin is not SubscriptionOrigin<TPos>.End)
            {
                var afterExclusive = resumeOrigin is SubscriptionOrigin<TPos>.After after
                    ? checked((long)after.Position.Value)
                    : -1;

                var events = _catchUpReader(_reader, afterExclusive, highWaterMark.Value, catchUpCts.Token);

                await foreach (var e in events.ConfigureAwait(false))
                {
                    last = _positionSelector(e.Context);
                    yield return SubscriptionMessage.Event(e);
                }
            }

            // With a filter, catching up may have looked past the last event it delivered, or past where the
            // subscriber was if it delivered none. A subscription without one is given every event, and has no need.
            if (highWaterMark is not null && _liveFilter is not AllEventsFilter)
            {
                var checkpoint = await _catchUpCheckpoint(_reader, highWaterMark.Value, catchUpCts.Token)
                    .ConfigureAwait(false);

                if (checkpoint is { } position && (last is null || position.Value > last.Value.Value))
                {
                    last = position;
                    yield return ToCheckpoint(position);
                }
            }
        }

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

        await foreach (var message in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (message.Event is { } e)
            {
                // Catching up has delivered it, if it was to be.
                if ((long)e.Context.LogPosition.Value <= highWaterMark)
                    continue;

                last = _positionSelector(e.Context);
            }
            else if (CheckpointOf(message) is { } checkpoint)
            {
                // A row from before the subscription caught up says nothing new, and must not take a subscriber back.
                if (last is not null && checkpoint.Value <= last.Value.Value)
                    continue;

                last = checkpoint;
            }

            yield return message;
        }
    }

    private static TPos? CheckpointOf(SubscriptionMessage<TEvent, string, StreamPosition, LogPosition> message)
    {
        return message switch
        {
            { LogCheckpoint: { HasValue: true, Value: TPos position } } => position,
            { StreamCheckpoint: { HasValue: true, Value: TPos position } } => position,
            _ => null
        };
    }

    private static SubscriptionMessage<TEvent, string, StreamPosition, LogPosition> ToCheckpoint(TPos position)
    {
        return position switch
        {
            LogPosition p => SubscriptionMessage.LogCheckpoint<TEvent, string, StreamPosition, LogPosition>(p),
            StreamPosition p => SubscriptionMessage.StreamCheckpoint<TEvent, string, StreamPosition, LogPosition>(p),
            _ => throw new UnreachableException($"Unexpected position type '{typeof(TPos).Name}'.")
        };
    }

    /// <summary>
    /// How far catching up has looked, in the position that the subscription resumes by, or <see langword="null"/> if
    /// there is nothing there to have looked at.
    /// </summary>
    public delegate Task<TPos?> CatchUpCheckpoint(
        EventLogReader<TEvent> reader,
        long highWaterMark,
        CancellationToken cancellationToken);

    public delegate IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> CatchUpReader(
        EventLogReader<TEvent> reader,
        long afterExclusive,
        long highWaterMark,
        CancellationToken cancellationToken);

    private enum RestartReason
    {
        QueueOverflow,
        FeedReset,
        Disposed
    }

    /// <summary>
    /// Buffers live events for one subscription cycle. Never blocks the feed: an overflow, like a feed reset, cancels
    /// the restart token and completes the channel so that the cycle ends and a new one starts from the last position.
    /// </summary>
    /// <remarks>
    /// With a filter, it also says how far it has looked. The feed notes the position of each row that the filter
    /// passes over, and a timer queues the last of them as a checkpoint. The feed queues an event before it notes any
    /// later position, so a checkpoint is in the queue behind every event that was selected before it, and a
    /// subscriber is never told of a position before it has been given what the filter selects up to there. The timer
    /// goes on after the log has gone quiet, so the last row that was passed over is reported too.
    /// </remarks>
    private sealed class Observer : IEventLogObserver<EventLogRow<TEvent>>, IDisposable
    {
        private const long Nowhere = -1;

        private readonly Channel<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> _channel;
        private readonly string? _streamId;
        private readonly EventFilter _liveFilter;
        private readonly Timer? _checkpointTimer;
        private readonly CancellationTokenSource _restartCts = new();
        private int _restartReason;
        private bool _hasFailed;
        private long _passedOver = Nowhere;
        private long _reported = Nowhere;
        private int _isReporting;

        public Observer(int queueCapacity, string? streamId, EventFilter liveFilter, TimeSpan checkpointInterval)
        {
            _streamId = streamId;
            _liveFilter = liveFilter;

            // The feed writes events, and the timer checkpoints. A checkpoint is only queued when the queue is
            // empty, so there is never more than one, and it has a place of its own: the queue always holds as many
            // events as it was asked to.
            var hasCheckpoints = liveFilter is not AllEventsFilter;

            _channel = Channel.CreateBounded<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>(
                new BoundedChannelOptions(hasCheckpoints ? queueCapacity + 1 : queueCapacity)
                {
                    SingleWriter = false,
                    SingleReader = true
                });

            // A subscription without a filter passes over nothing.
            if (hasCheckpoints)
            {
                _checkpointTimer = new Timer(
                    static x => ((Observer)x!).ReportPassedOver(),
                    this,
                    checkpointInterval,
                    checkpointInterval);
            }
        }

        public ChannelReader<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> Reader =>
            _channel.Reader;

        public CancellationToken RestartToken => _restartCts.Token;

        public RestartReason RestartReason => (RestartReason)Volatile.Read(ref _restartReason);

        public ValueTask OnNextAsync(EventLogRow<TEvent> row, CancellationToken cancellationToken)
        {
            // A subscription to a stream has nothing to say of the rest of the log. One that has failed is waiting to
            // say so, and a row that it could not queue would be taken for falling behind.
            if (_hasFailed || _streamId is not null && row.StreamId != _streamId)
                return ValueTask.CompletedTask;

            SubscriptionMessage<TEvent, string, StreamPosition, LogPosition> message;

            try
            {
                if (!_liveFilter.Matches(row))
                {
                    // In the position that the subscription resumes by.
                    Volatile.Write(ref _passedOver, _streamId is null ? row.Position : row.StreamPosition);
                    return ValueTask.CompletedTask;
                }

                // The row is only good for the length of this call, so the event is taken from it now. It is decoded
                // once, however many observers take it.
                message = SubscriptionMessage.Event(row.DecodedEvent);
            }
            catch (Exception ex)
            {
                // An event that cannot be decoded fails this subscription, as it would a read. Left to the feed, it
                // would be taken for a fault of the observer, which the feed detaches without a word.
                _hasFailed = true;
                _channel.Writer.TryComplete(ex);
                return ValueTask.CompletedTask;
            }

            if (!_channel.Writer.TryWrite(message))
                Restart(RestartReason.QueueOverflow);

            return ValueTask.CompletedTask;
        }

        // On the timer. With events in the queue there is nothing to add: each says where it is, and the next tick
        // will do. Nor is a checkpoint worth falling behind for, so one that cannot be queued is left for the next.
        private void ReportPassedOver()
        {
            // One tick at a time, should one ever be slow enough to meet the next.
            if (Interlocked.Exchange(ref _isReporting, 1) != 0)
                return;

            try
            {
                var position = Volatile.Read(ref _passedOver);

                if (position <= _reported || _channel.Reader.Count > 0)
                    return;

                var checkpoint = _streamId is null
                    ? SubscriptionMessage.LogCheckpoint<TEvent, string, StreamPosition, LogPosition>(
                        LogPosition.FromInt64(position))
                    : SubscriptionMessage.StreamCheckpoint<TEvent, string, StreamPosition, LogPosition>(
                        StreamPosition.FromInt64(position));

                if (_channel.Writer.TryWrite(checkpoint))
                    _reported = position;
            }
            finally
            {
                Volatile.Write(ref _isReporting, 0);
            }
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
            _checkpointTimer?.Dispose();

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