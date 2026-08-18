namespace DomainBlocks.EventStore.Abstractions.New;

public sealed class ReadStreamOptions2
{
    public static readonly ReadStreamOptions2 Default = new();

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