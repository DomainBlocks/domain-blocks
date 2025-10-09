namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the version of a stream. Versions are zero-based, increasing with each appended event. A version of
/// <see cref="None"/> (-1) indicates that the stream does not exist.
/// </summary>
public readonly struct StreamVersion : IEquatable<StreamVersion>, IComparable<StreamVersion>, IComparable
{
    /// <summary>
    /// Represents the absence of a version. Internally stored as -1. Indicates that a stream does not exist and has no
    /// committed events.
    /// </summary>
    public static readonly StreamVersion None = new(-1);

    private readonly long _value;

    private StreamVersion(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
        _value = value;
    }

    /// <summary>
    /// True if this instance equals <see cref="None"/>.
    /// </summary>
    public bool IsNone => this == None;

    /// <summary>
    /// Creates a <see cref="StreamVersion"/> from a 64-bit integer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="value"/> is less than -1.
    /// </exception>
    public static StreamVersion FromInt64(long value) => new(value);

    /// <summary>
    /// Returns the underlying 64-bit integer value.
    /// </summary>
    public long ToInt64() => _value;

    /// <summary>
    /// Converts a stream revision (i.e a specific version) to a UInt64 value.
    /// Though unlikely, the exception may be thrown if the input stream state is at None (-1) version and an
    /// attempt is made to convert that to a specific stream revision (greater than 0 value)
    /// Used in KurrentDb's implementation of IEventStore
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public ulong ToUint64()
    {
        if (_value < 0)
            throw new InvalidOperationException("Cannot convert StreamVersion 'None' to UInt64.");
        return (ulong)_value;
    }

    /// <summary>
    /// Returns the next stream version.
    /// </summary>
    /// <exception cref="OverflowException">
    /// The resulting version is greater than <see cref="Int64.MaxValue"/>.
    /// </exception>
    public StreamVersion Next() => Add(1);

    /// <summary>
    /// Returns a new <see cref="StreamVersion"/> incremented by <paramref name="value"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The resulting version is less than -1.</exception>
    /// <exception cref="OverflowException">
    /// The resulting version is greater than <see cref="Int64.MaxValue"/>.
    /// </exception>
    public StreamVersion Add(long value) => FromInt64(checked(_value + value));

    /// <summary>
    /// Returns a string representation of this version.
    /// </summary>
    public override string ToString() => this == None ? nameof(None) : _value.ToString();

    public bool Equals(StreamVersion other) => _value == other._value;

    public override bool Equals(object? obj) => obj is StreamVersion other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public int CompareTo(StreamVersion other) => _value.CompareTo(other._value);

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        return obj is StreamVersion other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(StreamVersion)}");
    }

    public static bool operator ==(StreamVersion left, StreamVersion right) => left.Equals(right);

    public static bool operator !=(StreamVersion left, StreamVersion right) => !left.Equals(right);

    public static bool operator <(StreamVersion left, StreamVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(StreamVersion left, StreamVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(StreamVersion left, StreamVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(StreamVersion left, StreamVersion right) => left.CompareTo(right) >= 0;
}