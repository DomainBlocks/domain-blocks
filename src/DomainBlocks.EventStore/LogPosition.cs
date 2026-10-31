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
    /// Gets an event's log position from its context.
    /// </summary>
    /// <typeparam name="TStreamId">The type of the stream ID.</typeparam>
    /// <param name="context">The event's context.</param>
    /// <returns>The event's log position.</returns>
    public static LogPosition FromContext<TStreamId>(ReadEventContext<TStreamId, StreamPosition, LogPosition> context)
        where TStreamId : notnull
    {
        return context.LogPosition;
    }

    /// <summary>
    /// Returns the position as a string.
    /// </summary>
    public override string ToString() => Value.ToString();
}