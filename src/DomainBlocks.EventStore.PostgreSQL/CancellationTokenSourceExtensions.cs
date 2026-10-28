using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class CancellationTokenSourceExtensions
{
    extension(CancellationTokenSource)
    {
        /// <summary>
        /// Gets the longest delay that <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> accepts, which is 1
        /// tick less than <see cref="uint.MaxValue"/> milliseconds.
        /// </summary>
        public static TimeSpan MaxSupportedDelay => TimeSpan.FromMilliseconds(uint.MaxValue) - TimeSpan.FromTicks(1);

        /// <summary>
        /// Throws if the delay is negative or longer than <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>
        /// accepts.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The delay is negative or longer than <c>MaxSupportedDelay</c>.
        /// </exception>
        public static void ThrowIfDelayOutOfRange(
            TimeSpan delay,
            [CallerArgumentExpression(nameof(delay))] string? paramName = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, paramName);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, CancellationTokenSource.MaxSupportedDelay, paramName);
        }
    }
}