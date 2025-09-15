using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamPosition : IEquatable<StreamPosition>
{
    public static readonly StreamPosition Start = new(StreamPositionKind.Start);
    public static readonly StreamPosition End = new(StreamPositionKind.End);

    private StreamPosition(StreamPositionKind kind, StreamVersion? version = null)
    {
        Kind = kind;
        Version = version;
    }

    public StreamPositionKind Kind { get; }

    public StreamVersion? Version { get; }

    public bool IsStart => Kind == StreamPositionKind.Start;

    public bool IsEnd => Kind == StreamPositionKind.End;

    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => Kind == StreamPositionKind.SpecificVersion;

    public static StreamPosition At(StreamVersion version)
    {
        return version == StreamVersion.None ? Start : new StreamPosition(StreamPositionKind.SpecificVersion, version);
    }

    public override string ToString() => Kind switch
    {
        StreamPositionKind.Start => nameof(Start),
        StreamPositionKind.End => nameof(End),
        _ => $"Version={Version?.ToString()}"
    };

    public bool Equals(StreamPosition other)
    {
        return Kind == other.Kind && Nullable.Equals(Version, other.Version);
    }

    public override bool Equals(object? obj)
    {
        return obj is StreamPosition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((int)Kind, Version);
    }

    public static bool operator ==(StreamPosition left, StreamPosition right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StreamPosition left, StreamPosition right)
    {
        return !left.Equals(right);
    }
}