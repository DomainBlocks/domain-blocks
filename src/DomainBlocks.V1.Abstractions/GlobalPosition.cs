namespace DomainBlocks.V1.Abstractions;

public readonly struct GlobalPosition
{
    public static readonly GlobalPosition Start = new(0UL);

    private readonly ulong _value;

    public GlobalPosition(ulong value)
    {
        _value = value;
    }

    public static GlobalPosition FromInt64(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be greater than or equal to zero.");

        return new GlobalPosition(Convert.ToUInt64(value));
    }

    public ulong ToUInt64() => _value;

    public long ToInt64() => Convert.ToInt64(_value);

    public override string ToString() => _value.ToString();
}