using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Checks durations against the ranges that .NET's timers support.
/// </summary>
internal static class TimerDurations
{
    /// <summary>
    /// The longest duration that <see cref="Timer"/>, <see cref="PeriodicTimer"/>, and
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> accept, which is 1 tick less than
    /// <see cref="uint.MaxValue"/> milliseconds.
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue) - TimeSpan.FromTicks(1);

    /// <summary>
    /// The shortest period that <see cref="PeriodicTimer"/> accepts, which is 1 millisecond.
    /// </summary>
    public static readonly TimeSpan MinPeriod = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Throws if the duration is longer than timers support.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The duration is longer than <see cref="MaxDuration"/>.</exception>
    public static void ThrowIfUnsupportedDuration(
        TimeSpan duration,
        [CallerArgumentExpression(nameof(duration))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duration, MaxDuration, paramName);
    }

    /// <summary>
    /// Throws if <see cref="PeriodicTimer"/> does not support the period.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The period is shorter than <see cref="MinPeriod"/> or longer than <see cref="MaxDuration"/>.
    /// </exception>
    public static void ThrowIfUnsupportedPeriod(
        TimeSpan period,
        [CallerArgumentExpression(nameof(period))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, MinPeriod, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(period, MaxDuration, paramName);
    }
}