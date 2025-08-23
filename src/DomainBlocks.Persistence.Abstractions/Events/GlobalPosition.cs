namespace DomainBlocks.Persistence.Abstractions.Events;

public readonly struct GlobalPosition
{
    public static readonly GlobalPosition Start = new(-1);
    public static readonly GlobalPosition End = new(long.MaxValue);

    private readonly long _value;

    private GlobalPosition(long value)
    {
        _value = value;
    }

    public static GlobalPosition FromUInt64(ulong value) => new(Convert.ToInt64(value));

    public static GlobalPosition At(StreamVersion version)
    {
        throw new NotImplementedException("TODO");
    }

    public static GlobalPosition Before(StreamVersion version)
    {
        throw new NotImplementedException("TODO");
    }

    public static GlobalPosition After(StreamVersion version)
    {
        throw new NotImplementedException("TODO");
    }
}