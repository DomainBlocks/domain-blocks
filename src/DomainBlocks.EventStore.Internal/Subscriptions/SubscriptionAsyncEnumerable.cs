using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Implements a catch-up subscription to the global event log or one event stream. It replays from a store's catch-up
/// source, then delivers live events from the store's feed.
/// </summary>
internal sealed class SubscriptionAsyncEnumerable<TEvent, TSourceEvent, TMark, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TSourceEvent : class, IFilterableEvent, IReadEvent<TEvent, string, StreamPosition, LogPosition>
    where TPos : struct, IPosition<TPos>
{
    private readonly IRefCountedFeed<TSourceEvent, TMark> _feed;
    private readonly ICatchUpSource<TEvent, TMark> _catchUpSource;
    private readonly EventFilter _liveFilter;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;
    private readonly ResiliencePipeline _retryPipeline;

    public SubscriptionAsyncEnumerable(
        IRefCountedFeed<TSourceEvent, TMark> feed,
        ICatchUpSource<TEvent, TMark> catchUpSource,
        EventFilter liveFilter,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        Func<Exception, bool> isTransient,
        ILogger? logger)
    {
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _feed = feed;
        _catchUpSource = catchUpSource;
        _liveFilter = liveFilter;
        _positionSelector = positionSelector;
        _logger = logger;

        _correlationId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();

        _retryPipeline = RetryPipeline.Create(
            isTransient,
            _options.CatchUpMaxRetryAttempts,
            _options.CatchUpRetryTimeout,
            (_, ex, attempt, delay) => _logger?.CatchUpRetrying(ex, _correlationId, attempt, delay));
    }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        SubscriptionObserver<TEvent, TSourceEvent>? observer = null;
        IFeedAttachment<TMark>? attachment = null;

        try
        {
            _logger?.SubscriptionStarted(_correlationId);

            var resumePosition = new ResumePosition(_origin switch
            {
                SubscriptionOrigin<TPos>.After after => ToInt64(after.Position),
                _ => null
            });

            var isCaughtUp = false;

            while (true)
            {
                var pinsEnd = observer is null && _origin is SubscriptionOrigin<TPos>.End;

                // Every cycle after the first is a restart. It is logged here because it can happen long after the
                // observer recorded the reason for it.
                if (observer is not null)
                {
                    if (resumePosition.After is { } after)
                        _logger?.SubscriptionRestarting(_correlationId, after);
                    else
                        _logger?.SubscriptionRestartingFromStart(_correlationId);
                }

                // The new observer is attached before the old one is detached, so the ref count never drops to zero and
                // the feed keeps its connection.
                var nextObserver = new SubscriptionObserver<TEvent, TSourceEvent>(
                    _options.QueueCapacity,
                    _liveFilter,
                    _logger,
                    _correlationId);

                var nextAttachment = await AttachObserverAsync(nextObserver, cancellationToken).ConfigureAwait(false);

                if (attachment is not null)
                    await attachment.DisposeAsync().ConfigureAwait(false);

                observer?.Dispose();
                observer = nextObserver;
                attachment = nextAttachment;

                var enumerator = ReadAllAsync(
                        resumePosition,
                        pinsEnd,
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

    private static long ToInt64(TPos position) => checked((long)position.Value);

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

    private async Task<IFeedAttachment<TMark>> AttachObserverAsync(
        SubscriptionObserver<TEvent, TSourceEvent> observer,
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
        bool pinsEnd,
        SubscriptionObserver<TEvent, TSourceEvent> observer,
        TMark highWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (var catchUp = await _retryPipeline
                   .ExecuteAsync(
                       static (s, ct) => s.Source.StartAsync(s.HighWaterMark, s.CorrelationId, ct),
                       (Source: _catchUpSource, HighWaterMark: highWaterMark, CorrelationId: _correlationId),
                       cancellationToken)
                   .ConfigureAwait(false))
        {
            var endInclusive = await _retryPipeline
                .ExecuteAsync(static (s, ct) => s.ReadEndAsync(ct), catchUp, cancellationToken)
                .ConfigureAwait(false);

            _logger?.CatchUpBoundary(_correlationId, endInclusive);

            if (endInclusive is { } end)
            {
                // A subscription from the end has nothing to replay on its first cycle.
                if (!pinsEnd)
                {
                    // After a transient error, the replay reads again after the last event that the subscriber took.
                    var events = ResumableAsyncEnumerable.Create(
                        static (s, after, ct) => s.CatchUp.ReadAsync(after, s.End, ct),
                        static (s, e) => ToInt64(s.Self._positionSelector(e.Context)),
                        (Self: this, CatchUp: catchUp, End: end),
                        resumePosition.After ?? -1,
                        _retryPipeline);

                    await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
                        yield return SubscriptionMessage.Event(e);
                }

                // The replay has covered the sequence up to the mark, even if the filter selected none of its events.
                // Moving the resume position here means the next cycle does not read that part of the sequence again.
                // It also pins the end for a subscription from the end. Otherwise, a restart before the first event is
                // delivered would resume from a later end and skip the events appended in between. If the sequence is
                // empty, the subscription resumes from the start.
                resumePosition.AdvanceTo(end);
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
            var position = ToInt64(_positionSelector(e.Context));

            // The replay has covered it.
            if (resumePosition.Covers(position))
                continue;

            yield return SubscriptionMessage.Event(e);

            // The subscriber has taken the event and come back for more.
            resumePosition.AdvanceTo(position);
        }
    }

    /// <summary>
    /// The furthest position delivered or covered by a replay. It only moves forward.
    /// </summary>
    private sealed class ResumePosition(long? after)
    {
        /// <summary>
        /// The position to resume after, or <see langword="null"/> to resume from the start.
        /// </summary>
        public long? After { get; private set; } = after;

        public bool Covers(long position) => After is { } after && position <= after;

        public void AdvanceTo(long position)
        {
            if (!Covers(position))
                After = position;
        }
    }
}