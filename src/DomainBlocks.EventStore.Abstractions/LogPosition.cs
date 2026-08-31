namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents an event position in the event log across all streams. Values increase in append order but may have gaps.
/// </summary>
/// <param name="Value">The non-negative log position.</param>
public readonly record struct LogPosition(ulong Value) : IPosition<LogPosition>
{
    /// <summary>
    /// Creates a log position from a non-negative 64-bit integer.
    /// </summary>
    /// <param name="value">The position value.</param>
    /// <returns>A log position containing <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static LogPosition FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new LogPosition((ulong)value);
    }

    /// <summary>
    /// Returns the numeric position value as a string.
    /// </summary>
    public override string ToString() => Value.ToString();
}