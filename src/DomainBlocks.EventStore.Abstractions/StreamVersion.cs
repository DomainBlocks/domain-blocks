namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamVersion : IEquatable<StreamVersion>, IComparable<StreamVersion>, IComparable
{
    public static readonly StreamVersion Zero = new(0);

    private readonly long _value;

    private StreamVersion(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
        _value = value;
    }

    public static StreamVersion FromInt64(long value) => new(value);

    public static StreamVersion FromUInt64(ulong value) => new(Convert.ToInt64(value));

    public long ToInt64() => _value;

    public ulong ToUInt64() => Convert.ToUInt64(_value);

    public override string ToString() => _value.ToString();

    public bool Equals(StreamVersion other)
    {
        return _value == other._value;
    }

    public override bool Equals(object? obj)
    {
        return obj is StreamVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public int CompareTo(StreamVersion other)
    {
        return _value.CompareTo(other._value);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        return obj is StreamVersion other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(StreamVersion)}");
    }

    public static bool operator ==(StreamVersion left, StreamVersion right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StreamVersion left, StreamVersion right)
    {
        return !left.Equals(right);
    }

    public static bool operator <(StreamVersion left, StreamVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(StreamVersion left, StreamVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(StreamVersion left, StreamVersion right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(StreamVersion left, StreamVersion right)
    {
        return left.CompareTo(right) >= 0;
    }
}