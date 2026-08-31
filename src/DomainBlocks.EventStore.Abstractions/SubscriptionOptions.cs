namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Configures an event subscription.
/// </summary>
public sealed record SubscriptionOptions
{
    /// <summary>
    /// The default subscription options.
    /// </summary>
    public static readonly SubscriptionOptions Default = new();

    /// <summary>
    /// The maximum number of events that can be buffered while the subscriber is consuming events. The default is
    /// 1,000.
    /// </summary>
    public int QueueCapacity { get; init; } = 1_000;
}