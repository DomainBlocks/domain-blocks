namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// A store-assigned, totally ordered position across all streams, if supported by the underlying event store.
/// </summary>
public readonly record struct GlobalPosition(ulong Value)
{
    public static GlobalPosition FromInt64(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Global position cannot be negative.");

        return new GlobalPosition((ulong)value);
    }
}