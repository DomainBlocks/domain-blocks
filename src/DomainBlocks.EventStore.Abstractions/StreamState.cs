using System.Diagnostics;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the observed state of an event stream.
/// </summary>
public readonly record struct StreamState<TVersion> where TVersion : notnull
{
    private const string VersionPrefix = "Version=";

    /// <summary>
    /// Represents a stream state indicating that the stream does not exist (i.e. has no events).
    /// </summary>
    public static readonly StreamState<TVersion> DoesNotExist = new(StreamStateKind.DoesNotExist);

    private readonly TVersion? _version;

    private StreamState(StreamStateKind kind, TVersion? version = default)
    {
        Kind = kind;
        _version = version;
    }

    /// <summary>
    /// Gets the kind of this stream state.
    /// </summary>
    public StreamStateKind Kind { get; }

    /// <summary>
    /// Gets the stream version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="StreamStateKind.AtVersion"/>.
    /// </exception>
    public TVersion Version => Kind == StreamStateKind.AtVersion
        ? _version!
        : throw new InvalidOperationException(
            $"Version is only available when Kind is '{nameof(StreamStateKind.AtVersion)}'. Kind: '{Kind}'.");

    /// <summary>
    /// Creates a stream state representing an existing stream with the specified version.
    /// </summary>
    public static StreamState<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new StreamState<TVersion>(StreamStateKind.AtVersion, version);
    }

    public override string ToString() => Kind switch
    {
        StreamStateKind.DoesNotExist => nameof(DoesNotExist),
        StreamStateKind.AtVersion => $"{VersionPrefix}{_version}",
        _ => throw new UnreachableException($"Unknown {nameof(StreamStateKind)} '{Kind}'.")
    };
}