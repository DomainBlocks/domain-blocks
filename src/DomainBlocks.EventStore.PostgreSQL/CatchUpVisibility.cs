using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Waits until queries can see what the feed has delivered.
/// </summary>
internal static class CatchUpVisibility
{
    private static readonly TimeSpan MinPollDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaxPollDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan WaitWarningInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits until queries can see the feed's high-water mark. Until the feed delivers a row, the mark is -1 and there
    /// is nothing to wait for.
    /// </summary>
    /// <remarks>
    /// The feed can deliver a row before queries can see it: the walsender sends a commit once it is flushed, and the
    /// commit becomes visible only later, after any wait for the synchronous standbys. Commits become visible in
    /// position order, and a later query sees at least as much, so once a query sees the position, the end that the
    /// catch-up reads next covers every row the observer missed.
    /// </remarks>
    public static async Task WaitAsync<TEvent>(
        EventLogReader<TEvent> reader,
        long highWaterMark,
        string correlationId,
        ILogger? logger,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        if (highWaterMark < 0)
            return;

        var delay = MinPollDelay;
        var stopwatch = Stopwatch.StartNew();
        var nextWarning = WaitWarningInterval;

        while ((await reader.GetMaxPositionAsync(cancellationToken).ConfigureAwait(false) ?? -1) < highWaterMark)
        {
            if (stopwatch.Elapsed >= nextWarning)
            {
                logger?.CatchUpWaitingForVisibility(correlationId, highWaterMark, stopwatch.Elapsed);
                nextWarning += WaitWarningInterval;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxPollDelay.Ticks));
        }
    }
}