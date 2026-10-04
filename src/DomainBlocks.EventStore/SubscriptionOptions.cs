using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore;

/// <summary>
/// Configures an event subscription.
/// </summary>
public sealed class SubscriptionOptions
{
    /// <summary>
    /// The default subscription options.
    /// </summary>
    public static readonly SubscriptionOptions Default = new();

    /// <summary>
    /// An optional identifier used to correlate log messages for a subscription. If provided, it must be unique among
    /// active subscriptions created by the same event store instance. If no value is provided, an unique identifier is
    /// generated.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// The maximum number of events that can be buffered while the subscriber is consuming events. The default is
    /// 1,000.
    /// </summary>
    public int QueueCapacity { get; init; } = 1_000;

    /// <summary>
    /// Selects the events to deliver. The default is <see cref="EventFilter.All"/>.
    /// </summary>
    public EventFilter Filter
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = EventFilter.All;
}