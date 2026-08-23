namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the zero-based position of an event within its stream.
/// </summary>
public readonly record struct StreamPosition(ulong Value) : IPosition<StreamPosition>
{
    public static StreamPosition FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        return new StreamPosition((ulong)value);
    }

    public long ToInt64() => checked((long)Value);
}