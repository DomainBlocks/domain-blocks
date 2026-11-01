namespace DomainBlocks.EventStore.Internal.Feeds;

internal sealed class FeedResumeOptions
{
    public static readonly FeedResumeOptions Default = new();

    /// <summary>
    /// Gets or sets the delay before the first retry, which grows exponentially, with jitter, up to
    /// <see cref="MaxRetryDelay"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the longest delay between retries.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how long a session must stay up to count as working. The failure that ends such a session starts a
    /// new outage, and the feed reconnects without a delay.
    /// </summary>
    public TimeSpan StabilityPeriod { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets the clock that times the delays and the stability period.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}