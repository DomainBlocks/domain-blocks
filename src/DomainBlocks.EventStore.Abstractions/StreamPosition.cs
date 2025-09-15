namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamPosition : IEquatable<StreamPosition>, IComparable<StreamPosition>, IComparable
{
    public static readonly StreamPosition Start = new(-1);
    public static readonly StreamPosition End = new(long.MaxValue);

    private readonly long _value;

    private StreamPosition(long value)
    {
        _value = value;
    }

    public static StreamPosition At(StreamVersion version)
    {
        throw new NotImplementedException("TODO");
    }

    public static StreamPosition Before(StreamVersion version)
    {
        throw new NotImplementedException("TODO");
    }

    public static StreamPosition After(StreamVersion version)
    {
        throw new NotImplementedException("TODO");
    }

    public long ToInt64() => _value;

    public bool Equals(StreamPosition other)
    {
        return _value == other._value;
    }

    public override bool Equals(object? obj)
    {
        return obj is StreamPosition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public static bool operator ==(StreamPosition left, StreamPosition right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StreamPosition left, StreamPosition right)
    {
        return !left.Equals(right);
    }

    public int CompareTo(StreamPosition other)
    {
        return _value.CompareTo(other._value);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        return obj is StreamPosition other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(StreamPosition)}");
    }

    public static bool operator <(StreamPosition left, StreamPosition right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(StreamPosition left, StreamPosition right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(StreamPosition left, StreamPosition right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(StreamPosition left, StreamPosition right)
    {
        return left.CompareTo(right) >= 0;
    }
}