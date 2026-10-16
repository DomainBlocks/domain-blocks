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
    /// Gets how long the tail waits for an append to be signalled before it reads the log anyway. This bounds the delay
    /// when a signal is missed. The default is 1 second.
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
    /// Gets the maximum number of events that one read of the log returns. The default is 1,000.
    /// </summary>
    public int MaxBatchSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_000;

    /// <summary>
    /// Gets the delay before the tail retries a read that failed with a transient error. The delay doubles, with
    /// jitter, on each consecutive failure, up to <see cref="MaxRetryDelay"/>. The default is 1 second.
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