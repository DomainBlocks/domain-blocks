using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// How much of the log each subscription in a fan-out benchmark selects.
/// </summary>
public enum FanOutSelection
{
    /// <summary>
    /// Every event, without a filter.
    /// </summary>
    All,

    /// <summary>
    /// The one percent of the events in one stream.
    /// </summary>
    OneStream,

    /// <summary>
    /// The same one percent of the events, selected by the value of a metadata entry.
    /// </summary>
    OneMetadataValue
}

internal static class FanOutSelections
{
    /// <summary>
    /// The number of streams that the events are spread evenly over, so that one stream holds one percent of them.
    /// </summary>
    public const int StreamCount = 100;

    /// <summary>
    /// Gets the ID of the stream that the event with the given index belongs to.
    /// </summary>
    public static string StreamIdOf(int index) => $"test-stream-{index % StreamCount}";

    /// <summary>
    /// Creates the filter that each subscription uses. Both one percent filters select the events of the first stream,
    /// whose <c>Value1</c> metadata entries are those of every hundredth event (see <see cref="TestEvents"/>).
    /// </summary>
    public static EventFilter CreateFilter(this FanOutSelection selection, int eventCount)
    {
        return selection switch
        {
            FanOutSelection.All => EventFilter.All,
            FanOutSelection.OneStream => EventFilter.StreamIds(StreamIdOf(0)),
            FanOutSelection.OneMetadataValue => EventFilter.Metadata(
                "Value1",
                Enumerable.Range(0, eventCount).Where(i => i % StreamCount == 0).Select(i => $"value1-{i}")),
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "Unknown selection.")
        };
    }
}