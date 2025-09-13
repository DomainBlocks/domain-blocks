namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamVersion : IEquatable<StreamVersion>, IComparable<StreamVersion>, IComparable
{
    public static readonly StreamVersion Zero = new(0);
    public static readonly StreamVersion None = new(-1);

    private readonly long _value;

    private StreamVersion(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
        _value = value;
    }

    public bool HasValue => this != None;

    public static StreamVersion FromInt64(long value) => new(value);

    public long ToInt64() => _value;

    public StreamVersion Next() => Add(1);

    public StreamVersion Add(long value) => FromInt64(checked(_value + value));

    public override string ToString() => this == None ? nameof(None) : _value.ToString();

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