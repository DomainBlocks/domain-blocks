namespace DomainBlocks.EventStore.Internal.Feeds;

internal sealed class FeedResumeOptions
{
    public static readonly FeedResumeOptions Default = new();

    /// <summary>
    /// Gets or sets how many times in a row the feed retries a failed attempt to create a session before it faults.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = int.MaxValue;

    /// <summary>
    /// Gets or sets the delay before the first retry, which grows exponentially, with jitter, up to
    /// <see cref="MaxRetryDelay"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the longest delay between retries.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}