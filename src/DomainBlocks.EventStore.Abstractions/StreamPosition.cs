namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamPosition : IEquatable<StreamPosition>, IComparable<StreamPosition>, IComparable
{
    public static readonly StreamPosition Start = new(0);
    public static readonly StreamPosition End = new(long.MaxValue);

    private readonly long _value;

    private StreamPosition(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
        _value = value;
    }

    public bool IsStart => this == Start;

    public bool IsEnd => this == End;

    public static StreamPosition At(StreamVersion version) => new(version.ToInt64());

    public static StreamPosition FromInt64(long value) => new(value);

    public static StreamPosition FromUInt64(ulong value) => new(Convert.ToInt64(value));

    public long ToInt64() => _value;

    public ulong ToUInt64() => Convert.ToUInt64(_value);

    public override string ToString()
    {
        if (this == Start) return nameof(Start);
        if (this == End) return nameof(End);
        return _value.ToString();
    }

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

    public static bool operator ==(StreamPosition left, StreamPosition right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StreamPosition left, StreamPosition right)
    {
        return !left.Equals(right);
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