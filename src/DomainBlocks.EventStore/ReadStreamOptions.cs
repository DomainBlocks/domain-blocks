using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore;

/// <summary>
/// Configures a stream read operation.
/// </summary>
public sealed record ReadStreamOptions
{
    /// <summary>
    /// The default stream read options.
    /// </summary>
    public static readonly ReadStreamOptions Default = new();

    /// <summary>
    /// The maximum number of events to read, or <see langword="null"/> for no limit (default). It must be
    /// positive.
    /// </summary>
    public int? MaxCount
    {
        get;
        init => field = value is null or > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The count must be positive.");
    }

    /// <summary>
    /// The behavior to apply when the requested stream does not exist. The default is
    /// <see cref="StreamNotFoundBehavior.Ignore"/>.
    /// </summary>
    public StreamNotFoundBehavior StreamNotFoundBehavior { get; init; }

    /// <summary>
    /// Specifies whether event metadata is included in the returned events. The default is <see langword="true"/>.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;

    /// <summary>
    /// Selects the events to read. The default is every event.
    /// </summary>
    public EventFilter Filter
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = EventFilter.All;

    /// <summary>
    /// How much of <see cref="Filter"/> the database is to evaluate. The default is as much as it can.
    /// </summary>
    public FilterPushdownMode FilterPushdownMode { get; init; }
}