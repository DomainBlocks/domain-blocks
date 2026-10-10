namespace DomainBlocks.EventStore;

/// <summary>
/// Represents the position of an event in the event log.
/// </summary>
/// <param name="Value">The position.</param>
public readonly record struct LogPosition(ulong Value) : IPosition<LogPosition>
{
    /// <summary>
    /// Creates a log position from a 64-bit signed integer.
    /// </summary>
    /// <param name="value">The position.</param>
    /// <returns>The log position <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static LogPosition FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new LogPosition((ulong)value);
    }

    /// <summary>
    /// Returns the position as a string.
    /// </summary>
    public override string ToString() => Value.ToString();
}