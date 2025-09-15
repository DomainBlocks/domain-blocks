using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

public readonly struct ExpectedStreamState : IEquatable<ExpectedStreamState>
{
    public static readonly ExpectedStreamState Any = new(ExpectedStreamStateKind.Any);

    public static readonly ExpectedStreamState StreamExists = new(ExpectedStreamStateKind.StreamExists);

    public static readonly ExpectedStreamState StreamDoesNotExist =
        new(ExpectedStreamStateKind.StreamDoesNotExist, StreamVersion.None);

    private ExpectedStreamState(ExpectedStreamStateKind kind, StreamVersion? version = null)
    {
        Kind = kind;
        Version = version;
    }

    public ExpectedStreamStateKind Kind { get; }

    public StreamVersion? Version { get; }

    public bool IsAny => Kind == ExpectedStreamStateKind.Any;

    public bool IsStreamExists => Kind == ExpectedStreamStateKind.StreamExists;

    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsStreamDoesNotExist => Kind == ExpectedStreamStateKind.StreamDoesNotExist;

    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => Kind == ExpectedStreamStateKind.SpecificVersion;

    public static ExpectedStreamState FromVersion(StreamVersion version)
    {
        return version == StreamVersion.None
            ? StreamDoesNotExist
            : new ExpectedStreamState(ExpectedStreamStateKind.SpecificVersion, version);
    }

    public override string ToString() => Kind switch
    {
        ExpectedStreamStateKind.Any => nameof(Any),
        ExpectedStreamStateKind.StreamExists => nameof(StreamExists),
        ExpectedStreamStateKind.StreamDoesNotExist => nameof(StreamDoesNotExist),
        _ => $"Version={Version?.ToString()}"
    };

    public bool Equals(ExpectedStreamState other)
    {
        return Kind == other.Kind && Nullable.Equals(Version, other.Version);
    }

    public override bool Equals(object? obj)
    {
        return obj is ExpectedStreamState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((int)Kind, Version);
    }

    public static bool operator ==(ExpectedStreamState left, ExpectedStreamState right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ExpectedStreamState left, ExpectedStreamState right)
    {
        return !left.Equals(right);
    }
}