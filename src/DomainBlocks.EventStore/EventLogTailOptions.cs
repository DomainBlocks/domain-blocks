namespace DomainBlocks.EventStore;

/// <summary>
/// Provides options for the tail that reads a store's event log for its live subscriptions.
/// </summary>
public sealed class EventLogTailOptions
{
    /// <summary>
    /// The default options.
    /// </summary>
    public static readonly EventLogTailOptions Default = new();

    /// <summary>
    /// Gets how long the tail waits to hear of an append before it reads the log anyway. This bounds the delay when an
    /// append is not heard. The default is 1 second.
    /// </summary>
    public TimeSpan PollInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the delay before the tail or a catch-up retries a read that failed with a transient error, or before the
    /// tail listens for appends again after listening failed. The delay doubles, with jitter, on each consecutive
    /// failure, up to <see cref="MaxRetryDelay"/>. The default is 1 second.
    /// </summary>
    public TimeSpan RetryDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the longest delay between retries. The default is 1 minute.
    /// </summary>
    public TimeSpan MaxRetryDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets how long the tail or a catch-up keeps retrying reads that fail with transient errors, from the first
    /// failure since it last made progress, before it gives up and the error fails its subscriptions. Handing on an
    /// event or completing a read counts as progress, so a connection that keeps dropping while events still arrive is
    /// not given up on. <see cref="Timeout.InfiniteTimeSpan"/> retries for as long as it takes. The default is 5
    /// minutes.
    /// </summary>
    public TimeSpan RetryTimeout
    {
        get;
        init
        {
            if (value != Timeout.InfiniteTimeSpan)
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);

            field = value;
        }
    } = TimeSpan.FromMinutes(5);
}