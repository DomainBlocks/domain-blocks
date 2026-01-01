namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Options for reading events from a stream.
/// </summary>
public sealed class ReadStreamOptions
{
    /// <summary>
    /// The default read stream options: read forward from the start with no maximum count.
    /// </summary>
    public static readonly ReadStreamOptions Default = new();

    /// <summary>
    /// The position at which to begin reading. Defaults to <see cref="StreamReadPosition.Start"/>.
    /// </summary>
    public StreamReadPosition Position { get; init; } = StreamReadPosition.Start;

    /// <summary>
    /// The direction in which to read events. Defaults to <see cref="StreamReadDirection.Forward"/>.
    /// </summary>
    public StreamReadDirection Direction { get; init; } = StreamReadDirection.Forward;

    /// <summary>
    /// The maximum number of events to read, or <c>null</c> for no limit.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Defines the behavior when the requested stream does not exist.
    /// </summary>
    public StreamNotFoundBehavior StreamNotFoundBehavior { get; init; }

    /// <summary>
    /// Gets a value indicating whether event metadata is included in the returned results. Excluding metadata can
    /// significantly reduce allocations and improve throughput for large stream reads.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;
}