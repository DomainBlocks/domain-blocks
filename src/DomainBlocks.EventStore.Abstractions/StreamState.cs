using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the observed state of an event stream.
/// </summary>
public readonly record struct StreamState
{
    /// <summary>
    /// Represents a stream state indicating that the stream does not exist (i.e. has no events).
    /// </summary>
    public static readonly StreamState StreamDoesNotExist = new(StreamStateKind.StreamDoesNotExist);

    private StreamState(StreamStateKind kind, StreamVersion? version = null)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// Gets the kind of this stream state.
    /// </summary>
    public StreamStateKind Kind { get; }

    /// <summary>
    /// Gets the stream version, if the stream exists.
    /// </summary>
    public StreamVersion? Version { get; }

    /// <summary>
    /// Gets a value indicating whether the stream does not exist.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Version))]
    public bool IsStreamDoesNotExist => Kind == StreamStateKind.StreamDoesNotExist;

    /// <summary>
    /// Gets a value indicating whether the stream exists.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsStreamExists => Kind == StreamStateKind.StreamExists;

    /// <summary>
    /// Creates a stream state representing an existing stream with the specified version.
    /// </summary>
    public static StreamState StreamExists(StreamVersion version) => new(StreamStateKind.StreamExists, version);

    public override string ToString() => Kind switch
    {
        StreamStateKind.StreamDoesNotExist => nameof(StreamDoesNotExist),
        _ => $"Version={Version?.Value}"
    };
}