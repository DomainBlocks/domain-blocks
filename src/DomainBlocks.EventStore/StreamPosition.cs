namespace DomainBlocks.EventStore;

/// <summary>
/// Represents the zero-based position of an event within its stream.
/// </summary>
/// <param name="Value">The position.</param>
/// <remarks>
/// Positions increase by one per event, without gaps.
/// </remarks>
public readonly record struct StreamPosition(ulong Value) : IPosition<StreamPosition>
{
    /// <summary>
    /// Creates a stream position from a 64-bit signed integer.
    /// </summary>
    /// <param name="value">The position.</param>
    /// <returns>The stream position <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static StreamPosition FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new StreamPosition((ulong)value);
    }

    /// <summary>
    /// Gets an event's stream position from its context.
    /// </summary>
    /// <typeparam name="TStreamId">The type of the stream ID.</typeparam>
    /// <param name="context">The event's context.</param>
    /// <returns>The event's stream position.</returns>
    public static StreamPosition FromContext<TStreamId>(
        ReadEventContext<TStreamId, StreamPosition, LogPosition> context)
        where TStreamId : notnull
    {
        return context.StreamPosition;
    }

    /// <summary>
    /// Returns the position as a string.
    /// </summary>
    public override string ToString() => Value.ToString();
}