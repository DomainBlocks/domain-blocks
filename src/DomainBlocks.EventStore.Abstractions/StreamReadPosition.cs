using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies where reading should begin within a stream. A position may represent the start of the stream,
/// the end of the stream, or a specific event version.
/// </summary>
public readonly struct StreamReadPosition : IEquatable<StreamReadPosition>
{
    /// <summary>
    /// Logical position at the start of the stream, before the first event.
    /// </summary>
    public static readonly StreamReadPosition Start = new(StreamReadPositionKind.Start);

    /// <summary>
    /// Logical position at the end of the stream, after the last event.
    /// </summary>
    public static readonly StreamReadPosition End = new(StreamReadPositionKind.End);

    private StreamReadPosition(StreamReadPositionKind kind, StreamVersion? version = null)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// The kind of stream position.
    /// </summary>
    public StreamReadPositionKind Kind { get; }

    /// <summary>
    /// The stream version when <see cref="Kind"/> is:
    /// <list type="bullet">
    ///   <item><see cref="StreamReadPositionKind.SpecificVersion"/> – a specific stream version.</item>
    ///   <item><see cref="StreamReadPositionKind.Start"/> – always <see cref="StreamVersion.None"/>.</item>
    /// </list>
    /// Otherwise, <c>null</c>.
    /// </summary>
    public StreamVersion? Version { get; }

    /// <summary>
    /// True if this position is <see cref="Start"/>.
    /// </summary>
    public bool IsStart => Kind == StreamReadPositionKind.Start;

    /// <summary>
    /// True if this position is <see cref="End"/>.
    /// </summary>
    public bool IsEnd => Kind == StreamReadPositionKind.End;

    /// <summary>
    /// True if this position is at a specific event version.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => Kind == StreamReadPositionKind.SpecificVersion;

    /// <summary>
    /// Creates a position at the given stream version. A version of <see cref="StreamVersion.None"/> is treated as
    /// <see cref="Start"/>.
    /// </summary>
    public static StreamReadPosition At(StreamVersion version)
    {
        return version == StreamVersion.None
            ? Start
            : new StreamReadPosition(StreamReadPositionKind.SpecificVersion, version);
    }

    /// <summary>
    /// Returns a string representation of this stream position.
    /// </summary>
    public override string ToString() => Kind switch
    {
        StreamReadPositionKind.Start => nameof(Start),
        StreamReadPositionKind.End => nameof(End),
        _ => $"Version={Version}"
    };

    public bool Equals(StreamReadPosition other) => Kind == other.Kind && Nullable.Equals(Version, other.Version);

    public override bool Equals(object? obj) => obj is StreamReadPosition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Kind, Version);

    public static bool operator ==(StreamReadPosition left, StreamReadPosition right) => left.Equals(right);

    public static bool operator !=(StreamReadPosition left, StreamReadPosition right) => !left.Equals(right);
}