namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal sealed class EventLogFeedOptions
{
    public static readonly EventLogFeedOptions Default = new();

    /// <summary>
    /// The maximum number of consecutive attempts to establish a session before the feed faults.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = int.MaxValue;

    /// <summary>
    /// The base delay between attempts to establish a session. Grows exponentially, with jitter, up to
    /// <see cref="MaxRetryDelay"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Decides whether a failure to establish or read a session is worth retrying.
    /// </summary>
    public Func<Exception, bool> IsTransient { get; set; } = EventLogFeedResumePolicy.IsTransient;
}
