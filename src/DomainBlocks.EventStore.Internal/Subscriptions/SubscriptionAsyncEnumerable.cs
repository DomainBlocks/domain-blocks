using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Implements a catch-up subscription to the global event log or one event stream. It replays through a store's
/// catch-up reader, then delivers live events from the store's feed.
/// </summary>
internal sealed class SubscriptionAsyncEnumerable<TEvent, TSourceEvent, TMark, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TSourceEvent : class, IFilterableEvent, IReadEvent<TEvent, string, StreamPosition, LogPosition>
    where TPos : struct, IPosition<TPos>
{
    private readonly IRefCountedFeed<TSourceEvent, TMark> _feed;
    private readonly CatchUpReader<TEvent, TMark, TPos> _catchUpReader;
    private readonly EventFilter _liveFilter;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public SubscriptionAsyncEnumerable(
        IRefCountedFeed<TSourceEvent, TMark> feed,
        IHistoryReader<TEvent, TMark> historyReader,
        EventFilter liveFilter,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        Func<Exception, bool> isTransient,
        ILogger? logger)
    {
        _feed = feed;
        _liveFilter = liveFilter;
        _positionSelector = positionSelector;
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _logger = logger;

        _correlationId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();

        _catchUpReader = new CatchUpReader<TEvent, TMark, TPos>(
            historyReader,
            positionSelector,
            _options,
            isTransient,
            _correlationId,
            logger);
    }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        SubscriptionObserver<TEvent, TSourceEvent>? observer = null;
        IFeedAttachment<TMark>? attachment = null;

        try
        {
            _logger?.SubscriptionStarted(_correlationId);

            observer = CreateObserver();
            attachment = await AttachObserverAsync(observer, cancellationToken).ConfigureAwait(false);

            var origin = _origin;
            var isCaughtUp = false;
            bool isRestarting;

            do
            {
                var catchUp = await ReadSnapshotAsync(attachment.HighWaterMark, origin, cancellationToken)
                    .ConfigureAwait(false);

                var position = catchUp.Position;

                _logger?.CatchUpBoundary(_correlationId, position?.Value);

                var events = catchUp.ReadAsync();

                await foreach (var e in WithLoggedExceptionsAsync(events, cancellationToken).ConfigureAwait(false))
                    yield return SubscriptionMessage.Event(e);

                // A restart signaled during the replay takes effect only now. Cancelling the replay would discard what
                // it read, and a replay slower than the queue's overflow would never finish.
                if (observer.StopReason == SubscriptionStopReason.None)
                {
                    _logger?.SubscriptionCaughtUp(_correlationId);

                    yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

                    isCaughtUp = true;

                    // The queue ends once the observer has stopped and everything in the queue has been read.
                    var liveEvents = observer.Reader.ReadAllAsync(cancellationToken);

                    await foreach (var e in WithLoggedExceptionsAsync(liveEvents, cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        var eventPosition = _positionSelector(e.Context);

                        // The replay has covered it.
                        if (eventPosition.Value <= position?.Value)
                            continue;

                        yield return SubscriptionMessage.Event(e);

                        // The subscriber has taken the event and come back for more.
                        position = eventPosition;
                    }
                }

                isRestarting = observer.StopReason
                    is SubscriptionStopReason.QueueOverflow
                    or SubscriptionStopReason.FeedReset;

                if (!isRestarting)
                    continue;

                if (isCaughtUp)
                {
                    if (observer.StopReason == SubscriptionStopReason.QueueOverflow)
                        _logger?.SubscriptionFellBehind(_correlationId);

                    isCaughtUp = false;
                    yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.FellBehind;
                }

                // The restart is logged here because it can happen long after the observer recorded the reason for
                // it.
                if (position is { } restartPosition)
                {
                    _logger?.SubscriptionRestarting(_correlationId, restartPosition.Value);
                    origin = SubscriptionOrigin.After(restartPosition);
                }
                else
                {
                    _logger?.SubscriptionRestartingFromStart(_correlationId);
                    origin = SubscriptionOrigin.Start;
                }

                // The new observer is attached before the old one is detached, so the ref count never drops to zero
                // and the feed keeps its connection.
                var nextObserver = CreateObserver();

                var nextAttachment = await AttachObserverAsync(nextObserver, cancellationToken)
                    .ConfigureAwait(false);

                await attachment.DisposeAsync().ConfigureAwait(false);
                observer.Dispose();

                observer = nextObserver;
                attachment = nextAttachment;
            } while (isRestarting);
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

    private SubscriptionObserver<TEvent, TSourceEvent> CreateObserver()
    {
        return new SubscriptionObserver<TEvent, TSourceEvent>(
            _options.QueueCapacity,
            _liveFilter,
            _logger,
            _correlationId);
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> WithLoggedExceptionsAsync(
        IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = events.GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            while (await MoveNextAsync(enumerator, cancellationToken).ConfigureAwait(false))
                yield return enumerator.Current;
        }
    }

    private async ValueTask<bool> MoveNextAsync(
        IAsyncEnumerator<ReadEvent<TEvent, string, StreamPosition, LogPosition>> enumerator,
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

    private async Task<CatchUpSnapshot<TEvent, TPos>> ReadSnapshotAsync(
        TMark highWaterMark,
        SubscriptionOrigin<TPos> origin,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _catchUpReader
                .ReadSnapshotAsync(highWaterMark, origin, cancellationToken)
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
}