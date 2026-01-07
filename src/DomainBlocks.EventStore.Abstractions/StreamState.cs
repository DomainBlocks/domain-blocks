using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamState : IEquatable<StreamState>
{
    public static readonly StreamState StreamDoesNotExist = new(Kind.StreamDoesNotExist);

    private readonly Kind _kind;

    private StreamState(Kind kind, StreamVersion? version = null)
    {
        _kind = kind;
        Version = version;
    }

    public StreamVersion? Version { get; }

    [MemberNotNullWhen(false, nameof(Version))]
    public bool IsStreamDoesNotExist => this == StreamDoesNotExist;

    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsStreamExists => _kind == Kind.StreamExists;

    public static StreamState StreamExists(StreamVersion version) => new(Kind.StreamExists, version);

    public override string ToString() => _kind switch
    {
        Kind.StreamDoesNotExist => nameof(StreamDoesNotExist),
        _ => $"Version={Version?.Value}"
    };

    public bool Equals(StreamState other) => _kind == other._kind && Nullable.Equals(Version, other.Version);

    public override bool Equals(object? obj) => obj is StreamState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_kind, Version);

    public static bool operator ==(StreamState left, StreamState right) => left.Equals(right);

    public static bool operator !=(StreamState left, StreamState right) => !left.Equals(right);

    private enum Kind
    {
        StreamDoesNotExist = 0,
        StreamExists
    }
}