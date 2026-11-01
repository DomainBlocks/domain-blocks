using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Implements a catch-up subscription to the global event log or one event stream. It replays a catch-up from a store's
/// reader, then delivers live events from the store's feed.
/// </summary>
/// <param name="feed">The store's feed of live events.</param>
/// <param name="catchUpReader">The reader of the subscription's history.</param>
/// <param name="liveFilter">Selects the live events to deliver.</param>
/// <param name="origin">Where the subscription starts, or <see langword="null"/> for the end.</param>
/// <param name="queueCapacity">The number of live events to queue before the subscription has to restart.</param>
/// <param name="correlationId">The subscription's reserved ID, which it releases when it stops.</param>
/// <param name="logger">The logger for the subscription's lifecycle.</param>
internal sealed class SubscriptionAsyncEnumerable<TEvent, TSourceEvent, TMark, TPos>(
    IRefCountedFeed<TSourceEvent, TMark> feed,
    CatchUpReader<TEvent, TPos, TMark> catchUpReader,
    EventFilter liveFilter,
    SubscriptionOrigin<TPos>? origin,
    int queueCapacity,
    string correlationId,
    ILogger? logger) :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TSourceEvent : class, IFilterableEvent, IReadEvent<TEvent, string, StreamPosition, LogPosition>
    where TPos : struct, IPosition<TPos>
{
    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        SubscriptionObserver<TEvent, TSourceEvent>? observer = null;
        IFeedAttachment<TMark>? attachment = null;

        try
        {
            logger?.SubscriptionStarted(correlationId);

            observer = CreateObserver();
            attachment = await AttachObserverAsync(observer, cancellationToken).ConfigureAwait(false);

            var highWaterMark = await WaitForConnectionAsync(attachment, cancellationToken).ConfigureAwait(false);

            // How far the subscription has got, which only moves forward. An origin can be past the end of the
            // sequence, and the events up to it aren't delivered when they arrive.
            TPos? position = origin is SubscriptionOrigin<TPos>.After after ? after.Position : null;

            var nextOrigin = origin ?? SubscriptionOrigin.End;
            var isCaughtUp = false;
            bool isRestarting;

            do
            {
                var catchUp = await ReadCatchUpAsync(nextOrigin, highWaterMark, cancellationToken)
                    .ConfigureAwait(false);

                // For a subscription from the end, this pins the end. Otherwise, a restart before the first event is
                // delivered would resume from a later end and skip the events appended in between.
                position = Max(position, catchUp.EndPosition);

                logger?.CatchUpBoundary(correlationId, position?.Value);

                await using (catchUp.ConfigureAwait(false))
                {
                    var events = catchUp.ReadAll();

                    await foreach (var e in WithLoggedExceptionsAsync(events, cancellationToken).ConfigureAwait(false))
                        yield return SubscriptionMessage.Event(e);
                }

                // A restart signaled during the replay takes effect only now. Cancelling the replay would discard what
                // it read, and a replay slower than the queue's overflow would never finish.
                if (observer.StopReason == SubscriptionStopReason.None)
                {
                    logger?.SubscriptionCaughtUp(correlationId);

                    yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

                    isCaughtUp = true;

                    // The queue ends once the observer has stopped and everything in the queue has been read.
                    var liveEvents = observer.Reader.ReadAllAsync(cancellationToken);

                    await foreach (var e in WithLoggedExceptionsAsync(liveEvents, cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        var eventPosition = TPos.FromContext(e.Context);

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
                        logger?.SubscriptionFellBehind(correlationId);

                    isCaughtUp = false;
                    yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.FellBehind;
                }

                // The restart is logged here because it can happen long after the observer recorded the reason for
                // it.
                if (position is { } restartPosition)
                {
                    logger?.SubscriptionRestarting(correlationId, restartPosition.Value);
                    nextOrigin = SubscriptionOrigin.After(restartPosition);
                }
                else
                {
                    // The sequence had no events when the end was read, and no live event has been delivered since.
                    logger?.SubscriptionRestartingFromStart(correlationId);
                    nextOrigin = SubscriptionOrigin.Start;
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

                highWaterMark = await WaitForConnectionAsync(attachment, cancellationToken).ConfigureAwait(false);
            } while (isRestarting);
        }
        finally
        {
            if (attachment is not null)
                await attachment.DisposeAsync().ConfigureAwait(false);

            observer?.Dispose();

            logger?.SubscriptionStopped(correlationId);
            CorrelationId.Release(correlationId);
        }
    }

    private static TPos? Max(TPos? first, TPos? second)
    {
        return second is null || first?.Value > second.Value.Value ? first : second;
    }

    private SubscriptionObserver<TEvent, TSourceEvent> CreateObserver()
    {
        return new SubscriptionObserver<TEvent, TSourceEvent>(
            queueCapacity,
            liveFilter,
            logger,
            correlationId);
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
            logger?.SubscriptionCanceled(correlationId);
            throw;
        }
        catch (Exception ex)
        {
            logger?.SubscriptionFailed(ex, correlationId);
            throw;
        }
    }

    private async Task<TMark> WaitForConnectionAsync(
        IFeedAttachment<TMark> attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            return await attachment.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger?.SubscriptionCanceled(correlationId);
            throw;
        }
        catch (Exception ex)
        {
            logger?.SubscriptionFailed(ex, correlationId);
            throw;
        }
    }

    private async Task<CatchUp<TEvent, TPos>> ReadCatchUpAsync(
        SubscriptionOrigin<TPos> catchUpOrigin,
        TMark highWaterMark,
        CancellationToken cancellationToken)
    {
        try
        {
            return await catchUpReader
                .ReadAsync(catchUpOrigin, highWaterMark, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger?.SubscriptionCanceled(correlationId);
            throw;
        }
        catch (Exception ex)
        {
            logger?.SubscriptionFailed(ex, correlationId);
            throw;
        }
    }

    private async Task<IFeedAttachment<TMark>> AttachObserverAsync(
        SubscriptionObserver<TEvent, TSourceEvent> observer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await feed.AttachAsync(observer, correlationId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            observer.Dispose();
            logger?.SubscriptionCanceled(correlationId);
            throw;
        }
        catch (Exception ex)
        {
            observer.Dispose();
            logger?.SubscriptionFailed(ex, correlationId);
            throw;
        }
    }
}