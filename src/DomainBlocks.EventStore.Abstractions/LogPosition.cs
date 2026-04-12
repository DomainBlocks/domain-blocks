namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// A store-assigned, totally ordered position across all streams, if supported by the underlying event store.
/// </summary>
public readonly record struct LogPosition(ulong Value)
{
    public static LogPosition FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        return new LogPosition((ulong)value);
    }
}