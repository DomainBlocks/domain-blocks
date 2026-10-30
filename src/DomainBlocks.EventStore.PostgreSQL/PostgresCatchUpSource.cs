using System.Diagnostics;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class PostgresCatchUpSource
{
    public static readonly TimeSpan MinVisibilityPollDelay = TimeSpan.FromMilliseconds(1);
    public static readonly TimeSpan MaxVisibilityPollDelay = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan VisibilityWaitWarningInterval = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Represents the history of the event log, or of one stream if a stream ID is given. A catch-up reads the end position
/// behind the replication barrier, once queries can see the feed's high-water mark.
/// </summary>
internal sealed class PostgresCatchUpSource<TEvent>(
    EventLogReader<TEvent> reader,
    string? streamId,
    EventFilter filter,
    ILogger? logger) :
    ICatchUpSource<TEvent, long>
    where TEvent : notnull
{
    // The subscription replays up to the end, or starts after it, so the read is followed by the replication barrier.
    public ValueTask<long?> ReadEndAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<long?>(streamId is null
            ? reader.ReadMaxPositionWithBarrierAsync(cancellationToken)
            : reader.ReadMaxStreamPositionWithBarrierAsync(streamId, cancellationToken));
    }

    public ValueTask<ICatchUp<TEvent>> StartAsync(
        long highWaterMark,
        string correlationId,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<ICatchUp<TEvent>>(new BarrierCatchUp(this, highWaterMark, correlationId));
    }

    private IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken)
    {
        return streamId is null
            ? reader.ReadCatchUpAllAsync(startExclusive, endInclusive, filter, cancellationToken)
            : reader.ReadCatchUpStreamAsync(streamId, startExclusive, endInclusive, filter, cancellationToken);
    }

    // The feed can deliver a row before queries can see it: the walsender sends a commit once it is flushed, and the
    // commit becomes visible only later, after any wait for the synchronous standbys. Commits become visible in
    // position order, and a later query sees at least as much, so once a query sees the position, the end that the
    // catch-up reads next covers every row the observer missed.
    private async Task WaitUntilVisibleAsync(long position, string correlationId, CancellationToken cancellationToken)
    {
        var delay = PostgresCatchUpSource.MinVisibilityPollDelay;
        var stopwatch = Stopwatch.StartNew();
        var nextWarning = PostgresCatchUpSource.VisibilityWaitWarningInterval;

        while ((await reader.GetMaxPositionAsync(cancellationToken).ConfigureAwait(false) ?? -1) < position)
        {
            if (stopwatch.Elapsed >= nextWarning)
            {
                logger?.CatchUpWaitingForVisibility(correlationId, position, stopwatch.Elapsed);
                nextWarning += PostgresCatchUpSource.VisibilityWaitWarningInterval;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, PostgresCatchUpSource.MaxVisibilityPollDelay.Ticks));
        }
    }

    /// <summary>
    /// Represents the reads of one catch-up, which wait until queries can see the high-water mark.
    /// </summary>
    private sealed class BarrierCatchUp(
        PostgresCatchUpSource<TEvent> source,
        long highWaterMark,
        string correlationId) :
        ICatchUp<TEvent>
    {
        public async ValueTask<long?> ReadEndAsync(CancellationToken cancellationToken)
        {
            // Until the feed delivers a row, the mark is -1 and there is nothing to wait for.
            if (highWaterMark >= 0)
            {
                await source
                    .WaitUntilVisibleAsync(highWaterMark, correlationId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await source.ReadEndAsync(cancellationToken).ConfigureAwait(false);
        }

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return source.ReadAsync(startExclusive, endInclusive, cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}