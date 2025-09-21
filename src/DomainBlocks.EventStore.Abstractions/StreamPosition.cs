using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents a position within a stream. A position may refer to the start of the stream, the end of the stream, or a
/// specific committed event version.
/// </summary>
public readonly struct StreamPosition : IEquatable<StreamPosition>
{
    /// <summary>
    /// Logical position at the start of the stream, before the first committed event.
    /// </summary>
    public static readonly StreamPosition Start = new(StreamPositionKind.Start);

    /// <summary>
    /// Logical position at the end of the stream, after the last committed event.
    /// </summary>
    public static readonly StreamPosition End = new(StreamPositionKind.End);

    private StreamPosition(StreamPositionKind kind, StreamVersion? version = null)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// The kind of stream position.
    /// </summary>
    public StreamPositionKind Kind { get; }

    /// <summary>
    /// The stream version when <see cref="Kind"/> is:
    /// <list type="bullet">
    ///   <item><see cref="StreamPositionKind.SpecificVersion"/> – a specific stream version.</item>
    ///   <item><see cref="StreamPositionKind.Start"/> – always <see cref="StreamVersion.None"/>.</item>
    /// </list>
    /// Otherwise, <c>null</c>.
    /// </summary>
    public StreamVersion? Version { get; }

    /// <summary>
    /// True if this position is <see cref="Start"/>.
    /// </summary>
    public bool IsStart => Kind == StreamPositionKind.Start;

    /// <summary>
    /// True if this position is <see cref="End"/>.
    /// </summary>
    public bool IsEnd => Kind == StreamPositionKind.End;

    /// <summary>
    /// True if this position is at a specific event version.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => Kind == StreamPositionKind.SpecificVersion;

    /// <summary>
    /// Creates a position at the given stream version. A version of <see cref="StreamVersion.None"/> is treated as
    /// <see cref="Start"/>.
    /// </summary>
    public static StreamPosition At(StreamVersion version)
    {
        return version == StreamVersion.None
            ? Start
            : new StreamPosition(StreamPositionKind.SpecificVersion, version);
    }

    /// <summary>
    /// Returns a string representation of this stream position.
    /// </summary>
    public override string ToString() => Kind switch
    {
        StreamPositionKind.Start => nameof(Start),
        StreamPositionKind.End => nameof(End),
        _ => $"Version={Version}"
    };

    public bool Equals(StreamPosition other) => Kind == other.Kind && Nullable.Equals(Version, other.Version);

    public override bool Equals(object? obj) => obj is StreamPosition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Kind, Version);

    public static bool operator ==(StreamPosition left, StreamPosition right) => left.Equals(right);

    public static bool operator !=(StreamPosition left, StreamPosition right) => !left.Equals(right);
}