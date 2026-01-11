namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the version of a stream. Versions are zero-based, increasing with each appended event.
/// </summary>
public readonly record struct StreamVersion(ulong Value)
{
    public static StreamVersion FromInt64(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Stream version cannot be negative.");

        return new StreamVersion((ulong)value);
    }

    public long ToInt64() => checked((long)Value);
}