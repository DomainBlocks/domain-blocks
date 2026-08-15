namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// A store-assigned number identifying an event's place in the global, append-only log - a totally ordered sequence
/// spanning all streams, if supported by the underlying event store. Values increase in append order but are not
/// guaranteed to be contiguous (some stores may leave gaps). The log-wide counterpart to <see cref="StreamVersion"/>.
/// </summary>
public readonly record struct LogSequenceNumber(ulong Value)
{
    public static LogSequenceNumber FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        return new LogSequenceNumber((ulong)value);
    }
}