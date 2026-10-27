namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Clamps the durations that the options accept to the ranges that .NET's timers support.
/// </summary>
internal static class TimerDurations
{
    // Timer, PeriodicTimer, and CancellationTokenSource.CancelAfter reject durations of uint.MaxValue milliseconds or
    // longer.
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    // PeriodicTimer also rejects periods shorter than 1 millisecond.
    private static readonly TimeSpan MinPeriod = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Returns the duration, or the longest one that timers support if it is longer.
    /// </summary>
    public static TimeSpan Clamp(TimeSpan duration) => TimeSpan.FromTicks(Math.Min(duration.Ticks, MaxDuration.Ticks));

    /// <summary>
    /// Returns the period, clamped to the range that <see cref="PeriodicTimer"/> supports.
    /// </summary>
    public static TimeSpan ClampPeriod(TimeSpan period) =>
        TimeSpan.FromTicks(Math.Clamp(period.Ticks, MinPeriod.Ticks, MaxDuration.Ticks));
}