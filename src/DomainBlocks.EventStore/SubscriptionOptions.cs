using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides options for a subscription.
/// </summary>
public sealed class SubscriptionOptions
{
    /// <summary>
    /// The default options.
    /// </summary>
    public static readonly SubscriptionOptions Default = new();

    /// <summary>
    /// Gets the ID that correlates the subscription's log messages, or <see langword="null"/> (the default) to generate
    /// one. The ID must be unique within the process, or subscribing throws an <see cref="InvalidOperationException"/>.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the maximum number of events buffered ahead of the subscriber. The default is 1,000.
    /// </summary>
    public int QueueCapacity { get; init; } = 1_000;

    /// <summary>
    /// Gets the filter that selects the events to deliver. The default is <see cref="EventFilter.All"/>.
    /// </summary>
    public EventFilter Filter
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = EventFilter.All;
}