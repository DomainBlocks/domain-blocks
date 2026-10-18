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
/// </remarks>
internal sealed class CatchUpSubscription<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TResultEvent, TStreamId, TStreamPos, TLogPos>>
    where TSourceEvent : IFilterableEvent, IReadEvent<TResultEvent, TStreamId, TStreamPos, TLogPos>
    where TResultEvent : notnull
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct, IComparable<TLogPos>
    where TPos : struct, IPosition<TPos>
{
    private readonly EventLogTail<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos> _tail;
    private readonly Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> _sourceFactory;
    private readonly ICatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos, TPos> _target;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public CatchUpSubscription(
        EventLogTail<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos> tail,
        Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> sourceFactory,
        ICatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos, TPos> target,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        ILogger? logger)
    {
        _tail = tail;
        _sourceFactory = sourceFactory;
        _target = target;
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _logger = logger;

        _correlationId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();
    }

    public async IAsyncEnumerator<SubscriptionMessage<TResultEvent, TStreamId, TStreamPos, TLogPos>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        SubscriptionQueue<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos>? queue = null;
        IAsyncDisposable? attachment = null;

        try
        {
            _logger?.SubscriptionStarted(_correlationId);

            var resumePosition = new ResumePosition(_origin);
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
                var nextQueue = new SubscriptionQueue<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos>(
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

                var enumerator = ReadCycleAsync(resumePosition, queue, cancellationToken)
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

                if (queue.StopReason != SubscriptionStopReason.QueueOverflow)
                    yield break;

                if (isCaughtUp)
                {
                    _logger?.SubscriptionFellBehind(_correlationId);

                    isCaughtUp = false;
                    yield return SubscriptionMessage<TResultEvent, TStreamId, TStreamPos, TLogPos>.FellBehind;
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
        IAsyncEnumerator<SubscriptionMessage<TResultEvent, TStreamId, TStreamPos, TLogPos>> enumerator,
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
        SubscriptionQueue<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos> queue,
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

    private async IAsyncEnumerable<SubscriptionMessage<TResultEvent, TStreamId, TStreamPos, TLogPos>> ReadCycleAsync(
        ResumePosition resumePosition,
        SubscriptionQueue<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos> queue,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = _sourceFactory();

        await using (source.ConfigureAwait(false))
        {
            var endInclusive = await _target.ReadEndPositionAsync(source, cancellationToken).ConfigureAwait(false);

            _logger?.CatchUpBoundary(_correlationId, endInclusive?.Value);

            // A subscription from the end resumes after the first end that a cycle reads, which is read after the
            // queue is attached. Resolving it any earlier would miss the events appended in between.
            resumePosition.ResolveEnd(endInclusive);

            if (endInclusive is { } end)
            {
                if (!resumePosition.Covers(end))
                {
                    var events = _target.ReadAsync(
                        source,
                        resumePosition.After,
                        end,
                        _options.Filter,
                        cancellationToken);

                    // Each event is valid only until the next is read, so it is decoded before it is handed on.
                    await foreach (var e in events.ConfigureAwait(false))
                        yield return SubscriptionMessage.Event(ReadEvent.Create(e.Payload, e.Metadata, e.Context));
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

        yield return SubscriptionMessage<TResultEvent, TStreamId, TStreamPos, TLogPos>.CaughtUp;

        // The queue ends once it has stopped and everything in it has been read.
        await foreach (var e in queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var position = _target.GetPosition(e.Context);

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
    /// Where the next cycle resumes: from the start, at the end, or after the furthest position delivered or covered by
    /// a replay. Once the end is resolved, it only moves forward.
    /// </summary>
    private sealed class ResumePosition(SubscriptionOrigin<TPos> origin)
    {
        private bool _isAtEnd = origin is SubscriptionOrigin<TPos>.End;

        /// <summary>
        /// The position to resume after, or <see langword="null"/> to resume from the start. It is
        /// <see langword="null"/> at the end until the end is resolved.
        /// </summary>
        public TPos? After { get; private set; } = origin is SubscriptionOrigin<TPos>.After after
            ? after.Position
            : null;

        /// <summary>
        /// Resolves a resume position at the end to the end that a cycle read, or to the start if the target was empty.
        /// It has no effect on any other resume position, or once the end is resolved.
        /// </summary>
        public void ResolveEnd(TPos? end)
        {
            if (!_isAtEnd)
                return;

            After = end;
            _isAtEnd = false;
        }

        public bool Covers(TPos position) => After is { } after && position.Value <= after.Value;

        public void AdvanceTo(TPos position)
        {
            if (!Covers(position))
                After = position;
        }
    }
}