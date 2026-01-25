using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the observed state of an event stream.
/// </summary>
public readonly record struct StreamState
{
    private const string VersionPrefix = "Version=";

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

    public static bool TryParse(string input, [NotNullWhen(true)] out StreamState? result)
    {
        result = null;

        if (string.Equals(input, StreamDoesNotExist.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            result = StreamDoesNotExist;
        }
        else if (input.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var raw = input[VersionPrefix.Length..];

            if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                result = StreamExists(new StreamVersion(v));
        }

        return result.HasValue;
    }

    public override string ToString() => Kind switch
    {
        StreamStateKind.StreamDoesNotExist => nameof(StreamDoesNotExist),
        _ => $"{VersionPrefix}{Version?.Value}"
    };
}