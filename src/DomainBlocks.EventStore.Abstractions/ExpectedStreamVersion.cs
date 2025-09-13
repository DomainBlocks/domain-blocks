namespace DomainBlocks.EventStore.Abstractions;

public readonly struct ExpectedStreamVersion : IEquatable<ExpectedStreamVersion>
{
    public static readonly ExpectedStreamVersion Any = new(0, ExpectedStreamVersionKind.Any);
    public static readonly ExpectedStreamVersion Exists = new(0, ExpectedStreamVersionKind.Exists);
    public static readonly ExpectedStreamVersion None = new(0, ExpectedStreamVersionKind.None);

    private readonly long _value; // Only meaningful when Kind == Specific

    private ExpectedStreamVersion(long value, ExpectedStreamVersionKind kind)
    {
        _value = value;
        Kind = kind;
    }

    public ExpectedStreamVersionKind Kind { get; }

    public static ExpectedStreamVersion FromVersion(StreamVersion version)
    {
        return version == StreamVersion.None
            ? None
            : new ExpectedStreamVersion(version.ToInt64(), ExpectedStreamVersionKind.Specific);
    }

    public StreamVersion ToVersion()
    {
        if (this == Any || this == Exists)
            throw new InvalidOperationException(
                $"Cannot convert ExpectedStreamVersion of kind '{Kind}' to a StreamVersion.");

        return this == None ? StreamVersion.None : StreamVersion.FromInt64(_value);
    }

    public override string ToString()
    {
        if (this == None) return nameof(None);
        if (this == Exists) return nameof(Exists);
        if (this == Any) return nameof(Any);
        return _value.ToString();
    }

    public bool Equals(ExpectedStreamVersion other)
    {
        return _value == other._value && Kind == other.Kind;
    }

    public override bool Equals(object? obj)
    {
        return obj is ExpectedStreamVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_value, (int)Kind);
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