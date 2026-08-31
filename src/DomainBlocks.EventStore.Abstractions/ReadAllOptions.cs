namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Options for configuring a read across all event streams.
/// </summary>
public sealed class ReadAllOptions
{
    /// <summary>
    /// The default options for reading across all streams.
    /// </summary>
    public static readonly ReadAllOptions Default = new();

    /// <summary>
    /// The maximum number of events to read, or <see langword="null"/> for no limit (default).
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Specifies whether event metadata is included in the returned events. The default is <see langword="true"/>.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;
}