using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Implements a catch-up subscription to the global event log or one event stream.
/// </summary>
/// <remarks>
/// A store derives from this class to supply its reads: the end position that a subscription from the end pins, and the
/// catch-up that each cycle replays through.
/// </remarks>
internal abstract class SubscriptionAsyncEnumerable<TEvent, TSourceEvent, TMark, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TSourceEvent : class, IFilterableEvent, IReadEvent<TEvent, string, StreamPosition, LogPosition>
    where TPos : struct, IPosition<TPos>
{
    private readonly IRefCountedFeed<TSourceEvent, TMark> _feed;
    private readonly EventFilter _liveFilter;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly ResiliencePipeline _retryPipeline;

    protected SubscriptionAsyncEnumerable(
        IRefCountedFeed<TSourceEvent, TMark> feed,
        EventFilter liveFilter,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        Func<Exception, bool> isTransient,
        ILogger? logger)
    {
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _liveFilter = liveFilter;
        _positionSelector = positionSelector;
        _feed = feed;
        _logger = logger;

        SubscriptionId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();

        _retryPipeline = RetryPipeline.Create(
            isTransient,
            _options.CatchUpMaxRetryAttempts,
            _options.CatchUpRetryTimeout,
            (_, ex, attempt, delay) => _logger?.CatchUpRetrying(ex, SubscriptionId, attempt, delay));
    }

    /// <summary>
    /// Gets the ID that correlates the subscription's log messages.
    /// </summary>
    protected string SubscriptionId { get; }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        SubscriptionObserver<TEvent, TSourceEvent>? observer = null;
        IFeedAttachment<TMark>? attachment = null;

        try
        {
            _logger?.SubscriptionStarted(SubscriptionId);

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
                        _logger?.SubscriptionRestarting(SubscriptionId, after.Value);
                    else
                        _logger?.SubscriptionRestartingFromStart(SubscriptionId);
                }

                // The new observer is attached before the old one is detached, so the ref count never drops to zero and
                // the feed keeps its connection.
                var nextObserver = new SubscriptionObserver<TEvent, TSourceEvent>(
                    _options.QueueCapacity,
                    _liveFilter,
                    _logger,
                    SubscriptionId);

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
                        _logger?.SubscriptionFellBehind(SubscriptionId);
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

            _logger?.SubscriptionStopped(SubscriptionId);
            CorrelationId.Release(SubscriptionId);
        }
    }

    /// <summary>
    /// Reads the end position of the followed sequence, or <see langword="null"/> if it is empty, when the subscription
    /// is not yet attached to the feed.
    /// </summary>
    protected abstract ValueTask<TPos?> ReadEndAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates the catch-up of one cycle, whose reads see every event up to the feed's high-water mark.
    /// </summary>
    protected abstract ValueTask<ICatchUp<TEvent, TPos>> CreateCatchUpAsync(
        TMark highWaterMark,
        CancellationToken cancellationToken);

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
            _logger?.SubscriptionCanceled(SubscriptionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.SubscriptionFailed(ex, SubscriptionId);
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
                    static (s, ct) => s.ReadEndAsync(ct),
                    this,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.SubscriptionCanceled(SubscriptionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.SubscriptionFailed(ex, SubscriptionId);
            throw;
        }
    }

    private async Task<IFeedAttachment<TMark>> AttachObserverAsync(
        SubscriptionObserver<TEvent, TSourceEvent> observer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _feed.AttachAsync(observer, SubscriptionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            observer.Dispose();
            _logger?.SubscriptionCanceled(SubscriptionId);
            throw;
        }
        catch (Exception ex)
        {
            observer.Dispose();
            _logger?.SubscriptionFailed(ex, SubscriptionId);
            throw;
        }
    }

    private async IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> ReadAllAsync(
        ResumePosition resumePosition,
        SubscriptionObserver<TEvent, TSourceEvent> observer,
        TMark highWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (var catchUp = await _retryPipeline
                   .ExecuteAsync(
                       static (s, ct) => s.Self.CreateCatchUpAsync(s.HighWaterMark, ct),
                       (Self: this, HighWaterMark: highWaterMark),
                       cancellationToken)
                   .ConfigureAwait(false))
        {
            var endInclusive = await _retryPipeline
                .ExecuteAsync(
                    static (s, ct) => s.ReadEndAsync(ct),
                    catchUp,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger?.CatchUpBoundary(SubscriptionId, endInclusive?.Value);

            if (endInclusive.HasValue)
            {
                var end = checked((long)endInclusive.Value.Value);

                // After a transient error, the replay reads again after the last event that the subscriber took.
                var events = ResumableAsyncEnumerable.Create(
                    static (s, after, ct) => ReadCatchUpAsync(s.CatchUp, after, s.End, ct),
                    static (s, e) => s.Self._positionSelector(e.Context),
                    (Self: this, CatchUp: catchUp, End: end),
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

        _logger?.SubscriptionCaughtUp(SubscriptionId);

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

    // Reads after the given position, or from the start if there is none.
    private static IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpAsync(
        ICatchUp<TEvent, TPos> catchUp,
        TPos? after,
        long endInclusive,
        CancellationToken cancellationToken)
    {
        var startExclusive = after is { } position ? checked((long)position.Value) : -1;
        return catchUp.ReadAsync(startExclusive, endInclusive, cancellationToken);
    }

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