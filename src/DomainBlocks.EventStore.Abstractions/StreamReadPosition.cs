using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies where reading should begin within a stream. A position may represent the start of the stream,
/// the end of the stream, or a specific event version. The default value is <see cref="Start"/>.
/// </summary>
public readonly record struct StreamReadPosition
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
    /// Gets the kind of this stream read position.
    /// </summary>
    public StreamReadPositionKind Kind { get; }

    /// <summary>
    /// The stream version when <see cref="IsSpecificVersion"/> is <c>true</c>, otherwise <c>null</c>.
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
    /// Creates a position at the given stream version.
    /// </summary>
    public static StreamReadPosition At(StreamVersion version) => new(StreamReadPositionKind.SpecificVersion, version);

    /// <summary>
    /// Returns a string representation of this stream position.
    /// </summary>
    public override string ToString() => Kind switch
    {
        StreamReadPositionKind.Start => nameof(Start),
        StreamReadPositionKind.End => nameof(End),
        _ => $"Version={Version?.Value}"
    };
}