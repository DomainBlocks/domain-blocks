namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Configures a stream read operation.
/// </summary>
public sealed class ReadStreamOptions
{
    /// <summary>
    /// The default stream read options.
    /// </summary>
    public static readonly ReadStreamOptions Default = new();

    /// <summary>
    /// The maximum number of events to read, or <see langword="null"/> for no limit (default).
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// The behavior to apply when the requested stream does not exist. The default is
    /// <see cref="StreamNotFoundBehavior.Ignore"/>.
    /// </summary>
    public StreamNotFoundBehavior StreamNotFoundBehavior { get; init; }

    /// <summary>
    /// Specifies whether event metadata is included in the returned events. The default is <see langword="true"/>.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;
}