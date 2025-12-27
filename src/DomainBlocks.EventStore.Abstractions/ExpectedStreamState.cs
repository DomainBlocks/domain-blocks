using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the expected state of a stream. Used to enforce concurrency or existence checks when performing stream
/// operations.
/// </summary>
public readonly struct ExpectedStreamState : IEquatable<ExpectedStreamState>
{
    /// <summary>
    /// Any state; stream may exist at any version or may not exist.
    /// </summary>
    public static readonly ExpectedStreamState Any = new(ExpectedStreamStateKind.Any);

    /// <summary>
    /// Stream must exist.
    /// </summary>
    public static readonly ExpectedStreamState StreamExists = new(ExpectedStreamStateKind.StreamExists);

    /// <summary>
    /// Stream must not exist.
    /// </summary>
    public static readonly ExpectedStreamState StreamDoesNotExist =
        new(ExpectedStreamStateKind.StreamDoesNotExist, StreamVersion.None);

    private ExpectedStreamState(ExpectedStreamStateKind kind, StreamVersion? version = null)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// The kind of expected state.
    /// </summary>
    public ExpectedStreamStateKind Kind { get; }

    /// <summary>
    /// The expected stream version when <see cref="Kind"/> is:
    /// <list type="bullet">
    ///   <item><see cref="ExpectedStreamStateKind.SpecificVersion"/> – a specific stream version.</item>
    ///   <item>
    ///     <see cref="ExpectedStreamStateKind.StreamDoesNotExist"/> – always <see cref="StreamVersion.None"/>.
    ///   </item>
    /// </list>
    /// Otherwise, <c>null</c>.
    /// </summary>
    public StreamVersion? Version { get; }

    /// <summary>
    /// True if <see cref="Kind"/> is <see cref="ExpectedStreamStateKind.Any"/>.
    /// </summary>
    public bool IsAny => Kind == ExpectedStreamStateKind.Any;

    /// <summary>
    /// True if <see cref="Kind"/> is <see cref="ExpectedStreamStateKind.StreamExists"/>.
    /// </summary>
    public bool IsStreamExists => Kind == ExpectedStreamStateKind.StreamExists;

    /// <summary>
    /// True if <see cref="Kind"/> is <see cref="ExpectedStreamStateKind.StreamDoesNotExist"/>.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsStreamDoesNotExist => Kind == ExpectedStreamStateKind.StreamDoesNotExist;

    /// <summary>
    /// True if <see cref="Kind"/> is <see cref="ExpectedStreamStateKind.SpecificVersion"/>.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => Kind == ExpectedStreamStateKind.SpecificVersion;

    /// <summary>
    /// Creates an expected stream state for a specific version. A version of
    /// <see cref="StreamVersion.None"/> is treated as <see cref="StreamDoesNotExist"/>.
    /// </summary>
    public static ExpectedStreamState FromVersion(StreamVersion version)
    {
        return version == StreamVersion.None
            ? StreamDoesNotExist
            : new ExpectedStreamState(ExpectedStreamStateKind.SpecificVersion, version);
    }

    /// <summary>
    /// Returns a string representation of this expected stream state.
    /// </summary>
    public override string ToString() => Kind switch
    {
        ExpectedStreamStateKind.Any => nameof(Any),
        ExpectedStreamStateKind.StreamExists => nameof(StreamExists),
        ExpectedStreamStateKind.StreamDoesNotExist => nameof(StreamDoesNotExist),
        _ => $"Version={Version}"
    };

    public bool Equals(ExpectedStreamState other) => Kind == other.Kind && Nullable.Equals(Version, other.Version);

    public override bool Equals(object? obj) => obj is ExpectedStreamState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Kind, Version);

    public static bool operator ==(ExpectedStreamState left, ExpectedStreamState right) => left.Equals(right);

    public static bool operator !=(ExpectedStreamState left, ExpectedStreamState right) => !left.Equals(right);
}