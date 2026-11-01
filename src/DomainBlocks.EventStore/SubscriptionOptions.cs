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
    /// Gets how long to retry the reads that start a subscription and catch it up, when they fail with transient errors
    /// and make no progress, before failing the subscription with the last error, or
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to retry without a limit. The default is 5 minutes.
    /// </summary>
    /// <remarks>
    /// Transient errors include a dropped connection and a failover. A read makes progress when it returns an event or
    /// completes, and the time runs from the first failure after the last progress. The reads stop retrying at this
    /// timeout or at <see cref="MaxRetryAttempts"/>, whichever they reach first.
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
    /// Gets how many times in a row to retry the reads that start a subscription and catch it up, when they fail with
    /// transient errors and make no progress, before failing the subscription with the last error. The default is
    /// <see cref="int.MaxValue"/>, which retries without a limit.
    /// </summary>
    /// <remarks>
    /// A read makes progress when it returns an event or completes, and the count starts again after progress. The
    /// reads stop retrying at this count or at <see cref="RetryTimeout"/>, whichever they reach first.
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