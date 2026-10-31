namespace DomainBlocks.EventStore;

public interface IPosition<out TSelf> where TSelf : struct, IPosition<TSelf>
{
    ulong Value { get; }

    /// <summary>
    /// Creates a position from a 64-bit signed integer.
    /// </summary>
    /// <param name="value">The position.</param>
    /// <returns>The position <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    static abstract TSelf FromInt64(long value);

    /// <summary>
    /// Gets an event's position of this type from its context.
    /// </summary>
    /// <typeparam name="TStreamId">The type of the stream ID.</typeparam>
    /// <param name="context">The event's context.</param>
    /// <returns>The event's position.</returns>
    static abstract TSelf FromContext<TStreamId>(ReadEventContext<TStreamId, StreamPosition, LogPosition> context)
        where TStreamId : notnull;
}