using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Implements a catch-up subscription to the global event log or one event stream.
/// </summary>
internal sealed class SubscriptionAsyncEnumerable<TEvent, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly EventLogReader<TEvent> _reader;
    private readonly IRefCountedFeed<EventLogDocument<TEvent>, BsonTimestamp> _feed;
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
        IRefCountedFeed<EventLogDocument<TEvent>, BsonTimestamp> feed,
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
            MongoTransientErrors.IsTransient,
            _options.CatchUpMaxRetryAttempts,
            _options.CatchUpRetryTimeout,
            (_, ex, attempt, delay) => _logger?.CatchUpRetrying(ex, _correlationId, attempt, delay));
    }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        SubscriptionObserver<TEvent, EventLogDocument<TEvent>>? observer = null;
        IFeedAttachment<BsonTimestamp>? attachment = null;

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
                // the change stream stays open.
                var nextObserver = new SubscriptionObserver<TEvent, EventLogDocument<TEvent>>(_options.QueueCapacity, _liveFilter, _logger, _correlationId);
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
                    case SubscriptionStopReason.QueueOverflow when isCaughtUp:
                        _logger?.SubscriptionFellBehind(_correlationId);
                        break;

                    case SubscriptionStopReason.QueueOverflow or SubscriptionStopReason.FeedReset:
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
                    static (s, ct) => s.ReadEndAsync(null, ct),
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

    private async Task<IFeedAttachment<BsonTimestamp>> AttachObserverAsync(
        SubscriptionObserver<TEvent, EventLogDocument<TEvent>> observer,
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
        SubscriptionObserver<TEvent, EventLogDocument<TEvent>> observer,
        BsonTimestamp opTimeHighWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Reads in the session wait for the change stream's high-water mark, so the end position and the replay include
        // every event that the observer won't receive from the stream.
        using (var session = await _retryPipeline
                   .ExecuteAsync(
                       static (s, ct) => new ValueTask<IClientSessionHandle>(
                           s.Reader.StartCatchUpSessionAsync(s.OpTimeHighWaterMark, ct)),
                       (Reader: _reader, OpTimeHighWaterMark: opTimeHighWaterMark),
                       cancellationToken)
                   .ConfigureAwait(false))
        {
            var endInclusive = await _retryPipeline
                .ExecuteAsync(
                    static (s, ct) => s.Self.ReadEndAsync(s.Session, ct),
                    (Self: this, Session: session),
                    cancellationToken)
                .ConfigureAwait(false);

            _logger?.CatchUpBoundary(_correlationId, endInclusive?.Value);

            if (endInclusive.HasValue)
            {
                var end = checked((long)endInclusive.Value.Value);

                // After a transient error, the replay reads again after the last event that the subscriber took.
                var events = ResumableAsyncEnumerable.Create(
                    static (s, after, ct) => s.Self.ReadCatchUpAsync(s.Session, after, s.End, ct),
                    static (s, e) => s.Self._positionSelector(e.Context),
                    (Self: this, Session: session, End: end),
                    resumePosition.After,
                    _retryPipeline);

                await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
                    yield return SubscriptionMessage.Event(e);

                // The replay has covered the sequence up to the mark, even if the filter selected none of its events.
                // Moving the resume position here means the next cycle does not read that part of the sequence again.
                resumePosition.AdvanceTo(endInclusive.Value);
            }
        }

        // A restart signaled during the replay takes effect only now. Cancelling the replay would discard what it read,
        // and a replay slower than the queue's overflow would never finish.
        if (observer.StopReason != SubscriptionStopReason.None)
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

    private ValueTask<TPos?> ReadEndAsync(IClientSessionHandle? session, CancellationToken cancellationToken)
    {
        return new ValueTask<TPos?>(_endPositionReader(_reader, session, cancellationToken));
    }

    // Reads after the given position, or from the start if there is none.
    private IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpAsync(
        IClientSessionHandle session,
        TPos? after,
        long endInclusive,
        CancellationToken cancellationToken)
    {
        var startExclusive = after is { } position ? checked((long)position.Value) : -1;
        return _catchUpReader(_reader, session, startExclusive, endInclusive, cancellationToken);
    }

    /// <summary>
    /// Reads the followed sequence from one position to another.
    /// </summary>
    public delegate IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> CatchUpReader(
        EventLogReader<TEvent> reader,
        IClientSessionHandle session,
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the end position of the followed sequence, or <see langword="null"/> if it is empty.
    /// </summary>
    public delegate Task<TPos?> EndPositionReader(
        EventLogReader<TEvent> reader,
        IClientSessionHandle? session,
        CancellationToken cancellationToken);

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
}