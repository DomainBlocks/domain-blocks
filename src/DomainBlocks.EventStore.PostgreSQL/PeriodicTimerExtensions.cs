using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class PeriodicTimerExtensions
{
    extension(PeriodicTimer)
    {
        /// <summary>
        /// Gets the shortest period that <see cref="PeriodicTimer"/> accepts, which is 1 millisecond.
        /// </summary>
        public static TimeSpan MinSupportedPeriod => TimeSpan.FromMilliseconds(1);

        /// <summary>
        /// Gets the longest period that <see cref="PeriodicTimer"/> accepts, which is 1 tick less than
        /// <see cref="uint.MaxValue"/> milliseconds.
        /// </summary>
        public static TimeSpan MaxSupportedPeriod => TimeSpan.FromMilliseconds(uint.MaxValue) - TimeSpan.FromTicks(1);

        /// <summary>
        /// Throws if <see cref="PeriodicTimer"/> does not accept the period.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The period is shorter than <c>MinSupportedPeriod</c> or longer than <c>MaxSupportedPeriod</c>.
        /// </exception>
        public static void ThrowIfOutOfRange(
            TimeSpan period,
            [CallerArgumentExpression(nameof(period))] string? paramName = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(period, PeriodicTimer.MinSupportedPeriod, paramName);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(period, PeriodicTimer.MaxSupportedPeriod, paramName);
        }
    }
}