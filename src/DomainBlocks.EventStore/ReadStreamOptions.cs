using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides options for a stream read.
/// </summary>
public sealed class ReadStreamOptions
{
    /// <summary>
    /// The default options.
    /// </summary>
    public static readonly ReadStreamOptions Default = new();

    /// <summary>
    /// Gets the maximum number of events to read, or <see langword="null"/> (the default) for no limit.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Gets what the read does if the stream does not exist. The default is
    /// <see cref="StreamNotFoundBehavior.Ignore"/>.
    /// </summary>
    public StreamNotFoundBehavior StreamNotFoundBehavior { get; init; }

    /// <summary>
    /// Gets a value that indicates whether to read event metadata. The default is <see langword="true"/>.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;

    /// <summary>
    /// Gets the filter that selects the events to read. The default is <see cref="EventFilter.All"/>.
    /// </summary>
    public EventFilter Filter
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = EventFilter.All;
}