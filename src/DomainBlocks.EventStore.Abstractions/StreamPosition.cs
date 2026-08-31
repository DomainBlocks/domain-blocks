namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the zero-based, contiguous position of an event within a stream. Positions increase by one for each
/// event without gaps. Unlike <see cref="LogPosition"/>, this position is scoped to a single stream.
/// </summary>
/// <param name="Value">The non-negative stream position.</param>
public readonly record struct StreamPosition(ulong Value) : IPosition<StreamPosition>
{
    /// <summary>
    /// Creates a stream position from a non-negative 64-bit integer.
    /// </summary>
    /// <param name="value">The position value.</param>
    /// <returns>A stream position containing <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static StreamPosition FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new StreamPosition((ulong)value);
    }

    /// <summary>
    /// Returns the numeric position value as a string.
    /// </summary>
    public override string ToString() => Value.ToString();
}