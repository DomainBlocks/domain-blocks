namespace DomainBlocks.Abstractions;

public readonly struct StreamVersion
{
    public static readonly StreamVersion Zero = new(0UL);

    private readonly ulong _value;

    public StreamVersion(ulong value)
    {
        _value = value;
    }

    public static StreamVersion FromInt32(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be greater than or equal to zero.");

        return new StreamVersion(Convert.ToUInt64(value));
    }

    public ulong ToUInt64() => _value;

    public override string ToString() => _value.ToString();
}