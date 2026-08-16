namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// A store-assigned number identifying an event's place in the global, append-only log - a totally ordered sequence
/// spanning all streams, if supported by the underlying event store. Values increase in append order but are not
/// guaranteed to be contiguous (some stores may leave gaps). The log-wide counterpart to <see cref="StreamPosition"/>.
/// </summary>
public readonly record struct LogPosition(ulong Value)
{
    public static LogPosition FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        return new LogPosition((ulong)value);
    }
}