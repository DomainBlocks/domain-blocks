using System.Diagnostics;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class PostgresHistoryReader
{
    public static readonly TimeSpan MinVisibilityPollDelay = TimeSpan.FromMilliseconds(1);
    public static readonly TimeSpan MaxVisibilityPollDelay = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan VisibilityWaitWarningInterval = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Represents the reads of the event log's history, or of one stream's if a stream ID is given. The end position is
/// read behind the replication barrier, once queries can see the feed's high-water mark.
/// </summary>
internal sealed class PostgresHistoryReader<TEvent>(
    EventLogReader<TEvent> reader,
    string? streamId,
    EventFilter filter,
    ILogger? logger) :
    IHistoryReader<TEvent, long>
    where TEvent : notnull
{
    public async ValueTask<IHistorySnapshot<TEvent>> ReadSnapshotAsync(
        long highWaterMark,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Until the feed delivers a row, the mark is -1 and there is nothing to wait for.
        if (highWaterMark >= 0)
            await WaitUntilVisibleAsync(highWaterMark, correlationId, cancellationToken).ConfigureAwait(false);

        // The subscription replays up to the end, or starts after it, so the read is followed by the replication
        // barrier.
        var end = streamId is null
            ? await reader.ReadMaxPositionWithBarrierAsync(cancellationToken).ConfigureAwait(false)
            : await reader.ReadMaxStreamPositionWithBarrierAsync(streamId, cancellationToken).ConfigureAwait(false);

        return new Snapshot(this, end);
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
        var delay = PostgresHistoryReader.MinVisibilityPollDelay;
        var stopwatch = Stopwatch.StartNew();
        var nextWarning = PostgresHistoryReader.VisibilityWaitWarningInterval;

        while ((await reader.GetMaxPositionAsync(cancellationToken).ConfigureAwait(false) ?? -1) < position)
        {
            if (stopwatch.Elapsed >= nextWarning)
            {
                logger?.CatchUpWaitingForVisibility(correlationId, position, stopwatch.Elapsed);
                nextWarning += PostgresHistoryReader.VisibilityWaitWarningInterval;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, PostgresHistoryReader.MaxVisibilityPollDelay.Ticks));
        }
    }

    /// <summary>
    /// Represents the sequence up to the end. The end bounds each read, since queries could already see it.
    /// </summary>
    private sealed class Snapshot(PostgresHistoryReader<TEvent> owner, long? end) : IHistorySnapshot<TEvent>
    {
        public long? End => end;

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return owner.ReadAsync(startExclusive, endInclusive, cancellationToken);
        }
    }
}