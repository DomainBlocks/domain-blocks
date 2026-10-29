using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Implements a catch-up subscription to the global event log or one event stream.
/// </summary>
internal sealed class SubscriptionAsyncEnumerable<TEvent, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private static readonly TimeSpan MinVisibilityPollDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaxVisibilityPollDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan VisibilityWaitWarningInterval = TimeSpan.FromSeconds(10);

    private readonly EventLogReader<TEvent> _reader;
    private readonly IRefCountedFeed<EventLogRow<TEvent>, long> _feed;
    private readonly CatchUpReader _catchUpReader;
    private readonly EndPositionReader _endPositionReader;
    private readonly EventFilter _liveFilter;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;
    private readonly ResiliencePipeline _retryPipeline;

    public SubscriptionAsyncEnumerable(
        EventLogReader<TEvent> reader,
        IRefCountedFeed<EventLogRow<TEvent>, long> feed,
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

        _retryPipeline = RetryPipeline.Create(
            PostgresTransientErrors.IsTransient,
            _options.CatchUpMaxRetryAttempts,
            _options.CatchUpRetryTimeout,
            (ex, attempt, delay) => _logger?.CatchUpRetrying(ex, _correlationId, attempt, delay));
    }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        Observer? observer = null;
        IFeedAttachment<long>? attachment = null;

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
                // Every cycle after the first is a restart. It is logged here because it can happen long after the
                // observer recorded the reason for it.
                if (observer is not null)
                {
                    if (resumePosition.After is { } after)
                        _logger?.SubscriptionRestarting(_correlationId, after.Value);
                    else
                        _logger?.SubscriptionRestartingFromStart(_correlationId);
                }

                // The new observer is attached before the old one is detached, so the ref count never drops to zero and
                // the replication session stays open.
                var nextObserver = new Observer(_options.QueueCapacity, _liveFilter, _logger, _correlationId);
                var nextAttachment = await AttachObserverAsync(nextObserver, cancellationToken).ConfigureAwait(false);

                if (attachment is not null)
                    await attachment.DisposeAsync().ConfigureAwait(false);

                observer?.Dispose();
                observer = nextObserver;
                attachment = nextAttachment;

                var enumerator = ReadAllAsync(
                        resumePosition,
                        observer,
                        attachment.HighWaterMark,
                        cancellationToken)
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

    // The end is pinned when the subscription starts. Otherwise, a restart before the first event is delivered would
    // resume from a later end and skip the events appended in between. If the sequence is empty, the subscription
    // resumes from the start.
    private async Task<TPos?> PinEndAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _retryPipeline
                .ExecuteAsync(
                    static (s, ct) => s.ReadEndAsync(-1, ct),
                    this,
                    cancellationToken)
                .ConfigureAwait(false);
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

    private async Task<IFeedAttachment<long>> AttachObserverAsync(
        Observer observer,
        CancellationToken cancellationToken)
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
        long positionHighWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endInclusive = await _retryPipeline
            .ExecuteAsync(
                static (s, ct) => s.Self.ReadEndAsync(s.PositionHighWaterMark, ct),
                (Self: this, PositionHighWaterMark: positionHighWaterMark),
                cancellationToken)
            .ConfigureAwait(false);

        _logger?.CatchUpBoundary(_correlationId, endInclusive?.Value);

        if (endInclusive.HasValue)
        {
            var end = checked((long)endInclusive.Value.Value);

            // After a transient error, the replay reads again after the last event that the subscriber took.
            var events = ResumableAsyncEnumerable.Create(
                static (s, after, ct) => s.Self.ReadCatchUpAsync(after, s.End, ct),
                static (s, e) => s.Self._positionSelector(e.Context),
                (Self: this, End: end),
                resumePosition.After,
                _retryPipeline);

            await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return SubscriptionMessage.Event(e);

            // The replay has covered the sequence up to the mark, even if the filter selected none of its events.
            // Moving the resume position here means the next cycle does not read that part of the sequence again.
            resumePosition.AdvanceTo(endInclusive.Value);
        }

        // A restart signaled during the replay takes effect only now. Cancelling the replay would discard what it read,
        // and a replay slower than the queue's overflow would never finish.
        if (observer.StopReason != StopReason.None)
            yield break;

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

        // The queue ends once the observer has stopped and everything in the queue has been read.
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

    private async ValueTask<TPos?> ReadEndAsync(long positionHighWaterMark, CancellationToken cancellationToken)
    {
        // Until the feed delivers a row, the mark is -1 and there is nothing to wait for.
        if (positionHighWaterMark >= 0)
            await WaitUntilVisibleAsync(positionHighWaterMark, cancellationToken).ConfigureAwait(false);

        return await _endPositionReader(_reader, cancellationToken).ConfigureAwait(false);
    }

    // Reads after the given position, or from the start if there is none.
    private IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpAsync(
        TPos? after,
        long endInclusive,
        CancellationToken cancellationToken)
    {
        var startExclusive = after is { } position ? checked((long)position.Value) : -1;
        return _catchUpReader(_reader, startExclusive, endInclusive, cancellationToken);
    }

    // The feed can deliver a row before queries can see it: the walsender sends a commit once it is flushed, and the
    // commit becomes visible only later, after any wait for the synchronous standbys. Commits become visible in
    // position order, and a later query sees at least as much, so once a query sees the position, the end that the
    // catch-up reads next covers every row the observer missed.
    private async Task WaitUntilVisibleAsync(long position, CancellationToken cancellationToken)
    {
        var delay = MinVisibilityPollDelay;
        var stopwatch = Stopwatch.StartNew();
        var nextWarning = VisibilityWaitWarningInterval;

        while ((await _reader.GetMaxPositionAsync(cancellationToken).ConfigureAwait(false) ?? -1) < position)
        {
            if (stopwatch.Elapsed >= nextWarning)
            {
                _logger?.CatchUpWaitingForVisibility(_correlationId, position, stopwatch.Elapsed);
                nextWarning += VisibilityWaitWarningInterval;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxVisibilityPollDelay.Ticks));
        }
    }

    /// <summary>
    /// Reads the followed sequence from one position to another.
    /// </summary>
    public delegate IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> CatchUpReader(
        EventLogReader<TEvent> reader,
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the end position of the followed sequence, or <see langword="null"/> if it is empty. The subscription
    /// replays up to the end, or starts after it, so the read must be followed by the replication barrier.
    /// </summary>
    public delegate Task<TPos?> EndPositionReader(EventLogReader<TEvent> reader, CancellationToken cancellationToken);

    /// <summary>
    /// The furthest position delivered or covered by a replay. It only moves forward.
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

    internal enum StopReason
    {
        None,
        QueueOverflow,
        FeedReset,
        Disposed
    }

    /// <summary>
    /// Buffers the live events that a subscription selects, for one cycle. It never blocks the feed. On a queue
    /// overflow or a feed reset, it records the reason and completes its queue, and the cycle restarts once the queue
    /// is drained, which can be much later.
    /// </summary>
    internal sealed class Observer(
        int queueCapacity,
        EventFilter liveFilter,
        ILogger? logger = null,
        string subscriptionId = "") :
        IFeedObserver<EventLogRow<TEvent>>,
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
            // A stopped observer stays attached until the subscription replaces it, so it skips rows instead of
            // decoding events that would be dropped.
            if (_isStopped || !liveFilter.Matches(row))
                return ValueTask.CompletedTask;

            ReadEvent<TEvent, string, StreamPosition, LogPosition> e;

            try
            {
                // The row is only valid during this call.
                e = ReadEvent.Create(row.Payload, row.Metadata, row.Context);
            }
            catch (Exception ex)
            {
                // An event that cannot be decoded fails this subscription, as it would fail a read. If the exception
                // reached the feed, the feed would detach the observer without telling the subscriber.
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

        // Only the first reason is kept, and the result says whether this call set it. The reason is recorded before
        // the queue completes, so it can be read once the queue ends.
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