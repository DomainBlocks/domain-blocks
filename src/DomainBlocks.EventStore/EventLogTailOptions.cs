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
    /// Gets the delay before the tail retries a read that failed with a transient error, or listens for appends again
    /// after listening failed. The delay doubles, with jitter, on each consecutive failure, up to
    /// <see cref="MaxRetryDelay"/>. The default is 1 second.
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
}