namespace DomainBlocks.V1.Abstractions;

public readonly struct StreamPosition
{
    public static readonly StreamPosition Start = new(0UL);

    private readonly ulong _value;

    public StreamPosition(ulong value)
    {
        _value = value;
    }

    public static StreamPosition FromInt32(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be greater than or equal to zero.");

        return new StreamPosition(Convert.ToUInt64(value));
    }

    public ulong ToUInt64() => _value;

    public override string ToString() => _value.ToString();
}