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
}