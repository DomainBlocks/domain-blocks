using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Implements a catch-up subscription to the global event log or one event stream, over a store's tail.
/// </summary>
/// <remarks>
/// <para>
/// Each cycle attaches a queue to the tail, reads the target's end position through a source of its own, and replays
/// the target up to it, then delivers the queued live events that the replay did not cover. The end is read after the
/// queue is attached, so together they cover every event.
/// </para>
/// <para>
/// When the queue overflows, the cycle ends once the queue is drained, and the next cycle resumes after the last event
/// that was delivered or covered.
/// </para>
/// <para>
/// A transient error in the catch-up's reads is retried with backoff, as the tail retries its own. A replay resumes
/// after the last event it handed on, up to the same end, so no event is missed or delivered twice. Any other error
/// fails the subscription.
/// </para>
/// </remarks>
internal sealed class CatchUpSubscription<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>>
    where TEvent : notnull
    where TSourceEvent : IFilterableEvent, IReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct, IComparable<TLogPos>
    where TPos : struct, IPosition<TPos>
{
    private readonly EventLogTail<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> _tail;
    private readonly Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> _sourceFactory;
    private readonly ICatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos, TPos> _target;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly EventLogTailOptions _retryOptions;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public CatchUpSubscription(
        EventLogTail<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> tail,
        Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> sourceFactory,
        ICatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos, TPos> target,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        EventLogTailOptions retryOptions,
        ILogger? logger)
    {
        _tail = tail;
        _sourceFactory = sourceFactory;
        _target = target;
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _retryOptions = retryOptions;
        _logger = logger;

        _correlationId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();
    }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        SubscriptionQueue<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos>? queue = null;
        IAsyncDisposable? attachment = null;

        try
        {
            _logger?.SubscriptionStarted(_correlationId);

            var resumePosition = new ResumePosition(_origin is SubscriptionOrigin<TPos>.After after
                ? after.Position
                : null);

            // An origin at the end is resolved by the first cycle's catch-up, which reads the end after the queue is
            // attached. Resolving it any earlier would miss the events appended in between.
            var resumeFromEnd = _origin is SubscriptionOrigin<TPos>.End;
            var isCaughtUp = false;

            while (true)
            {
                // Every cycle after the first is a restart. It is logged here because it can happen long after the
                // queue recorded the reason for it.
                if (queue is not null)
                {
                    if (resumePosition.After is { } position)
                        _logger?.SubscriptionRestarting(_correlationId, position.Value);
                    else
                        _logger?.SubscriptionRestartingFromStart(_correlationId);
                }

                // The new queue is attached before the old one is detached, so the tail keeps running between cycles.
                var nextQueue = new SubscriptionQueue<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos>(
                    _options.QueueCapacity,
                    Selects,
                    _logger,
                    _correlationId);
                var nextAttachment = await AttachAsync(nextQueue, cancellationToken).ConfigureAwait(false);

                if (attachment is not null)
                    await attachment.DisposeAsync().ConfigureAwait(false);

                queue?.Dispose();
                queue = nextQueue;
                attachment = nextAttachment;

                var enumerator = ReadCycleAsync(resumePosition, resumeFromEnd, queue, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                resumeFromEnd = false;

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

                if (queue.StopReason != SubscriptionStopReason.QueueOverflow)
                    yield break;

                if (isCaughtUp)
                {
                    _logger?.SubscriptionFellBehind(_correlationId);

                    isCaughtUp = false;
                    yield return SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>.FellBehind;
                }
            }
        }
        finally
        {
            if (attachment is not null)
                await attachment.DisposeAsync().ConfigureAwait(false);

            queue?.Dispose();

            _logger?.SubscriptionStopped(_correlationId);
            CorrelationId.Release(_correlationId);
        }
    }

    private async ValueTask<bool> MoveNextAsync(
        IAsyncEnumerator<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> enumerator,
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

    private async Task<IAsyncDisposable> AttachAsync(
        SubscriptionQueue<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> queue,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _tail.AttachAsync(queue, _correlationId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            queue.Dispose();
            _logger?.SubscriptionCanceled(_correlationId);
            throw;
        }
        catch (Exception ex)
        {
            queue.Dispose();
            _logger?.SubscriptionFailed(ex, _correlationId);
            throw;
        }
    }

    private async IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> ReadCycleAsync(
        ResumePosition resumePosition,
        bool resumeFromEnd,
        SubscriptionQueue<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> queue,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = _sourceFactory();

        await using (source.ConfigureAwait(false))
        {
            var pipeline = SubscriptionRetry.CreatePipeline(
                source.IsTransientError,
                _retryOptions,
                (ex, attempt, delay) => _logger?.SubscriptionRetrying(ex, _correlationId, attempt, delay));

            var endInclusive = await pipeline
                .ExecuteAsync(
                    static (state, ct) => state.Target.ReadEndPositionAsync(state.Source, ct),
                    (Target: _target, Source: source),
                    cancellationToken)
                .ConfigureAwait(false);

            _logger?.CatchUpBoundary(_correlationId, endInclusive?.Value);

            if (endInclusive is { } end)
            {
                if (!resumeFromEnd && !resumePosition.Covers(end))
                {
                    var replay = new Replay(_target, source, resumePosition.After, end, _options.Filter);

                    await using (replay.ConfigureAwait(false))
                    {
                        // Each event is valid only until the next is read, so it is decoded before it is handed on.
                        while (await pipeline
                                   .ExecuteAsync(static (r, ct) => r.MoveNextAsync(ct), replay, cancellationToken)
                                   .ConfigureAwait(false))
                        {
                            var e = replay.Current;
                            yield return SubscriptionMessage.Event(ReadEvent.Create(e.Payload, e.Metadata, e.Context));
                        }
                    }
                }

                // The replay has covered the log up to the end position, even if the filter selected none of its
                // events. Moving the resume position here means the next cycle does not read that part again.
                resumePosition.AdvanceTo(end);
            }
        }

        // A restart signaled during the replay takes effect only now. Cancelling the replay would discard what it read,
        // and a replay slower than the queue's overflow would never finish.
        if (queue.StopReason != SubscriptionStopReason.None)
            yield break;

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>.CaughtUp;

        // The queue ends once it has stopped and everything in it has been read.
        await foreach (var e in queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var position = _target.PositionOf(e.Context);

            // The replay has covered it.
            if (resumePosition.Covers(position))
                continue;

            yield return SubscriptionMessage.Event(e);

            // The subscriber has taken the event and come back for more.
            resumePosition.AdvanceTo(position);
        }
    }

    private bool Selects(TSourceEvent sourceEvent)
    {
        return _target.Contains(sourceEvent.Context) && _options.Filter.Matches(sourceEvent);
    }

    /// <summary>
    /// A replay of the target up to an end position, which reads again after the last event it handed on when a read
    /// fails, so that a retry neither misses nor repeats an event.
    /// </summary>
    /// <remarks>
    /// Each step is one execution of the retry pipeline, so its backoff starts again once the replay has made progress.
    /// </remarks>
    private sealed class Replay(
        ICatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos, TPos> target,
        IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> source,
        TPos? after,
        TPos endInclusive,
        EventFilter filter) :
        IAsyncDisposable
    {
        private IAsyncEnumerator<TSourceEvent>? _events;
        private bool _hasCurrent;

        public TSourceEvent Current => _events!.Current;

        public async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
        {
            // The current event has been handed on, so a read after a failure starts after it.
            if (_hasCurrent)
            {
                after = target.PositionOf(_events!.Current.Context);
                _hasCurrent = false;
            }

            _events ??= target
                .ReadAsync(source, after, endInclusive, filter, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            try
            {
                _hasCurrent = await _events.MoveNextAsync().ConfigureAwait(false);
                return _hasCurrent;
            }
            catch
            {
                // The failed read is abandoned, so the next attempt opens a new one.
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_events is null)
                return;

            var events = _events;
            _events = null;
            await events.DisposeAsync().ConfigureAwait(false);
        }
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