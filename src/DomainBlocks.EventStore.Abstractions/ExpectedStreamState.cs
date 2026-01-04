using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the expected state of a stream. Used to enforce concurrency or existence checks when performing stream
/// operations. The default value is <see cref="Any"/>.
/// </summary>
public readonly struct ExpectedStreamState : IEquatable<ExpectedStreamState>
{
    private readonly Kind _kind;

    /// <summary>
    /// Any state; stream may exist at any version or may not exist.
    /// </summary>
    public static readonly ExpectedStreamState Any = new(Kind.Any);

    /// <summary>
    /// Stream must exist.
    /// </summary>
    public static readonly ExpectedStreamState StreamExists = new(Kind.StreamExists);

    /// <summary>
    /// Stream must not exist.
    /// </summary>
    public static readonly ExpectedStreamState StreamDoesNotExist = new(Kind.StreamDoesNotExist);

    private ExpectedStreamState(Kind kind, StreamVersion? version = null)
    {
        _kind = kind;
        Version = version;
    }

    /// <summary>
    /// The expected stream version when <see cref="IsSpecificVersion"/> is <c>true</c>, otherwise <c>null</c>.
    /// </summary>
    public StreamVersion? Version { get; }

    /// <summary>
    /// True if this instance is <see cref="Any"/>.
    /// </summary>
    public bool IsAny => this == Any;

    /// <summary>
    /// True if this instance is <see cref="StreamExists"/>.
    /// </summary>
    public bool IsStreamExists => this == StreamExists;

    /// <summary>
    /// True if this instance is <see cref="StreamDoesNotExist"/>.
    /// </summary>
    public bool IsStreamDoesNotExist => this == StreamDoesNotExist;

    /// <summary>
    /// True if this instance represents a specific stream version.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => _kind == Kind.SpecificVersion;

    /// <summary>
    /// Creates an expected stream state for a specific version.
    /// </summary>
    public static ExpectedStreamState SpecificVersion(StreamVersion version)
    {
        return new ExpectedStreamState(Kind.SpecificVersion, version);
    }

    /// <summary>
    /// Returns a string representation of this expected stream state.
    /// </summary>
    public override string ToString() => _kind switch
    {
        Kind.Any => nameof(Any),
        Kind.StreamExists => nameof(StreamExists),
        Kind.StreamDoesNotExist => nameof(StreamDoesNotExist),
        _ => $"Version={Version?.Value}"
    };

    public bool Equals(ExpectedStreamState other) => _kind == other._kind && Nullable.Equals(Version, other.Version);

    public override bool Equals(object? obj) => obj is ExpectedStreamState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_kind, Version);

    public static bool operator ==(ExpectedStreamState left, ExpectedStreamState right) => left.Equals(right);

    public static bool operator !=(ExpectedStreamState left, ExpectedStreamState right) => !left.Equals(right);

    private enum Kind
    {
        Any = 0,
        StreamExists,
        StreamDoesNotExist,
        SpecificVersion
    }
}