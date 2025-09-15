namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamVersion
{
    public static readonly StreamVersion Zero = new(0);

    private readonly ulong _value;

    private StreamVersion(ulong value)
    {
        _value = value;
    }

    public static StreamVersion FromUInt64(ulong value) => new(value);

    public static StreamVersion FromInt64(long value) => new(Convert.ToUInt64(value));

    public ulong ToUInt64() => _value;

    public long ToInt64() => Convert.ToInt64(_value);
}