namespace DomainBlocks.EventStore.Abstractions;

public readonly struct GlobalPosition
{
    public static readonly GlobalPosition Start = new(0);
    public static readonly GlobalPosition End = new(long.MaxValue);

    private readonly long _value;

    private GlobalPosition(long value)
    {
        _value = value;
    }

    public static GlobalPosition FromInt64(long value) => new(value);

    public static GlobalPosition FromUInt64(ulong value) => new(Convert.ToInt64(value));

    public long ToInt64() => _value;

    public ulong ToUInt64() => Convert.ToUInt64(_value);
}