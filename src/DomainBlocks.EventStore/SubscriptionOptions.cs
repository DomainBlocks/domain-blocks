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
    /// Gets how long the subscription tries to recover from transient errors before it fails with the last error, or
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to keep trying without a limit. The default is 5
    /// minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transient errors include a dropped connection and a failover. The subscription fails at this timeout or at
    /// <see cref="MaxRetryAttempts"/>, whichever it reaches first. Both apply to what the subscription depends on at
    /// the time.
    /// </para>
    /// <para>
    /// While it catches up, they apply to its reads. A read makes progress when it returns an event or completes, and
    /// the time runs from the first failure after the last progress. The store's live feed may fail meanwhile without
    /// counting, because it may recover before the replay ends.
    /// </para>
    /// <para>
    /// Until the feed first connects, and once the subscription is live, they apply to the feed, which the store's
    /// subscriptions share and which retries without a limit. The time runs from the first failure that the
    /// subscription sees. The feed recovers only when a connection stays up for 2 minutes. Events don't count, so a
    /// connection that keeps dropping fails the subscription even while events arrive. Each restart of the subscription
    /// starts again, such as after it falls behind.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is zero, or negative and not <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    public TimeSpan RetryTimeout
    {
        get;
        init
        {
            if (value != System.Threading.Timeout.InfiniteTimeSpan)
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);

            field = value;
        }
    } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets how many retries the subscription allows when recovering from transient errors, before it fails with the
    /// last error. The default is <see cref="int.MaxValue"/>, which retries without a limit.
    /// </summary>
    /// <remarks>
    /// <see cref="RetryTimeout"/> says what the limits apply to, and when they start again. A count applies to the
    /// subscription's own reads, or to the retries of the live feed, whose backoff sets their pace. So
    /// <see cref="RetryTimeout"/> is the better limit on how long the subscription waits for the feed.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxRetryAttempts
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = int.MaxValue;

    /// <summary>
    /// Gets the filter that selects the events to deliver. The default is <see cref="EventFilter.All"/>.
    /// </summary>
    public EventFilter Filter
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = EventFilter.All;
}