using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies where reading should begin within a stream. A position may represent the start of the stream,
/// the end of the stream, or a specific event version. The default value is <see cref="Start"/>.
/// </summary>
public readonly struct StreamReadPosition : IEquatable<StreamReadPosition>
{
    private readonly Kind _kind;

    /// <summary>
    /// Logical position at the start of the stream, before the first event.
    /// </summary>
    public static readonly StreamReadPosition Start = new(Kind.Start);

    /// <summary>
    /// Logical position at the end of the stream, after the last event.
    /// </summary>
    public static readonly StreamReadPosition End = new(Kind.End);

    private StreamReadPosition(Kind kind, StreamVersion? version = null)
    {
        _kind = kind;
        Version = version;
    }

    /// <summary>
    /// The stream version when <see cref="IsSpecificVersion"/> is <c>true</c>, otherwise <c>null</c>.
    /// </summary>
    public StreamVersion? Version { get; }

    /// <summary>
    /// True if this position is <see cref="Start"/>.
    /// </summary>
    public bool IsStart => this == Start;

    /// <summary>
    /// True if this position is <see cref="End"/>.
    /// </summary>
    public bool IsEnd => this == End;

    /// <summary>
    /// True if this position is at a specific event version.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => _kind == Kind.SpecificVersion;

    /// <summary>
    /// Creates a position at the given stream version.
    /// </summary>
    public static StreamReadPosition At(StreamVersion version) => new(Kind.SpecificVersion, version);

    /// <summary>
    /// Returns a string representation of this stream position.
    /// </summary>
    public override string ToString() => _kind switch
    {
        Kind.Start => nameof(Start),
        Kind.End => nameof(End),
        _ => $"Version={Version?.Value}"
    };

    public bool Equals(StreamReadPosition other) => _kind == other._kind && Nullable.Equals(Version, other.Version);

    public override bool Equals(object? obj) => obj is StreamReadPosition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_kind, Version);

    public static bool operator ==(StreamReadPosition left, StreamReadPosition right) => left.Equals(right);

    public static bool operator !=(StreamReadPosition left, StreamReadPosition right) => !left.Equals(right);

    private enum Kind
    {
        Start = 0,
        End,
        SpecificVersion
    }
}