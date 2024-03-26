namespace DomainBlocks.Persistence;

public readonly struct StreamVersion
{
    public static readonly StreamVersion Zero = new(0UL);

    private readonly ulong _value;

    private StreamVersion(ulong value)
    {
        _value = value;
    }

    public static StreamVersion FromUInt64(ulong value) => new(value);

    public ulong ToUInt64() => _value;

    public override string ToString() => _value.ToString();
}