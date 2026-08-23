namespace DomainBlocks.EventStore.Abstractions;

public sealed class ReadAllOptions
{
    public static readonly ReadAllOptions Default = new();

    /// <summary>
    /// The maximum number of events to read, or <c>null</c> for no limit.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether event metadata is included in the returned results. Excluding metadata can
    /// significantly reduce allocations and improve throughput for large stream reads.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;
}