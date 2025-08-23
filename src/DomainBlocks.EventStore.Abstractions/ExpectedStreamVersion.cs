namespace DomainBlocks.EventStore.Abstractions;

public readonly struct ExpectedStreamVersion : IEquatable<ExpectedStreamVersion>
{
    public static readonly ExpectedStreamVersion None = new(-1);
    public static readonly ExpectedStreamVersion Exists = new(-2);
    public static readonly ExpectedStreamVersion Any = new(-3);

    private readonly long _value;

    private ExpectedStreamVersion(long value)
    {
        _value = value;
    }

    public static ExpectedStreamVersion At(long version)
    {
        return new ExpectedStreamVersion(version);
    }

    public static ExpectedStreamVersion At(StreamVersion version)
    {
        throw new NotImplementedException("TODO");
    }

    public long ToInt64() => _value;

    public bool Equals(ExpectedStreamVersion other)
    {
        return _value == other._value;
    }

    public override bool Equals(object? obj)
    {
        return obj is ExpectedStreamVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public static bool operator ==(ExpectedStreamVersion left, ExpectedStreamVersion right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ExpectedStreamVersion left, ExpectedStreamVersion right)
    {
        return !left.Equals(right);
    }
}