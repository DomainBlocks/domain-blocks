using System.Diagnostics;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Implements a catch-up subscription whose replay reads the end position behind the replication barrier, once queries
/// can see the feed's high-water mark.
/// </summary>
internal sealed class PostgresSubscription<TEvent, TPos>(
    EventLogReader<TEvent> reader,
    IRefCountedFeed<EventLogRow<TEvent>, long> feed,
    PostgresSubscription<TEvent, TPos>.CatchUpReader catchUpReader,
    PostgresSubscription<TEvent, TPos>.EndPositionReader endPositionReader,
    EventFilter liveFilter,
    Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
    SubscriptionOrigin<TPos>? origin,
    SubscriptionOptions? options,
    ILogger? logger) :
    SubscriptionAsyncEnumerable<TEvent, EventLogRow<TEvent>, long, TPos>(
        feed,
        liveFilter,
        positionSelector,
        origin,
        options,
        PostgresTransientErrors.IsTransient,
        logger)
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private static readonly TimeSpan MinVisibilityPollDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaxVisibilityPollDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan VisibilityWaitWarningInterval = TimeSpan.FromSeconds(10);

    private readonly EventLogReader<TEvent> _reader = reader;
    private readonly CatchUpReader _catchUpReader = catchUpReader;
    private readonly ILogger? _logger = logger;

    protected override ValueTask<TPos?> ReadEndAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<TPos?>(endPositionReader(_reader, cancellationToken));
    }

    protected override ValueTask<ICatchUp<TEvent, TPos>> CreateCatchUpAsync(
        long highWaterMark,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<ICatchUp<TEvent, TPos>>(new BarrierCatchUp(this, highWaterMark));
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
                _logger?.CatchUpWaitingForVisibility(SubscriptionId, position, stopwatch.Elapsed);
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
    /// Represents the reads of one catch-up, which wait until queries can see the high-water mark.
    /// </summary>
    private sealed class BarrierCatchUp(PostgresSubscription<TEvent, TPos> subscription, long highWaterMark) :
        ICatchUp<TEvent, TPos>
    {
        public async ValueTask<TPos?> ReadEndAsync(CancellationToken cancellationToken)
        {
            // Until the feed delivers a row, the mark is -1 and there is nothing to wait for.
            if (highWaterMark >= 0)
                await subscription.WaitUntilVisibleAsync(highWaterMark, cancellationToken).ConfigureAwait(false);

            return await subscription.ReadEndAsync(cancellationToken).ConfigureAwait(false);
        }

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return subscription._catchUpReader(subscription._reader, startExclusive, endInclusive, cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}