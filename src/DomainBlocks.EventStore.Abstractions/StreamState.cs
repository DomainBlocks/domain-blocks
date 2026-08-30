namespace DomainBlocks.EventStore.Abstractions;

public static class StreamState
{
    public static StreamState<TVersion> DoesNotExist<TVersion>() where TVersion : notnull =>
        StreamState<TVersion>.DoesNotExist;

    public static StreamState<TVersion> AtVersion<TVersion>(TVersion version) where TVersion : notnull =>
        StreamState<TVersion>.AtVersion(version);
}

/// <summary>
/// Represents the observed state of an event stream.
/// </summary>
public readonly record struct StreamState<TVersion> where TVersion : notnull
{
    /// <summary>
    /// Represents a stream state indicating that the stream does not exist (i.e. has no events).
    /// </summary>
    public static readonly StreamState<TVersion> DoesNotExist = new(StreamStateKind.DoesNotExist);

    private StreamState(StreamStateKind kind, TVersion? version = default)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// Gets the kind of this stream state.
    /// </summary>
    public StreamStateKind Kind { get; }

    public bool HasVersion => Kind == StreamStateKind.AtVersion;

    /// <summary>
    /// Gets the stream version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="StreamStateKind.AtVersion"/>.
    /// </exception>
    public TVersion Version => HasVersion
        ? field!
        : throw new InvalidOperationException("Stream state has no version.");

    /// <summary>
    /// Creates a stream state representing an existing stream with the specified version.
    /// </summary>
    public static StreamState<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new StreamState<TVersion>(StreamStateKind.AtVersion, version);
    }

    public override string ToString() => HasVersion ? $"Version={Version}" : Kind.ToString();
}