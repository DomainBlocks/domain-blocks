namespace DomainBlocks.EventStore.PostgreSQL;

public sealed class PostgresEventStoreOptions
{
    /// <summary>
    /// Gets or sets the schema of the store's tables and functions, which must match <c>^[a-z_][a-z0-9_]{0,62}$</c>.
    /// The default is <c>dbx</c>.
    /// </summary>
    public string Schema { get; set; } = "dbx";

    /// <summary>
    /// Gets or sets the maximum number of queued append requests before callers wait. The default is 1,000.
    /// </summary>
    public int AppendQueueCapacity { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets the maximum number of append requests committed in one round trip. The default is 500.
    /// </summary>
    public int AppendMaxBatchSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets the maximum time to wait for more append requests when a batch is not full. The default is
    /// <see cref="TimeSpan.Zero"/>, which commits without waiting.
    /// </summary>
    public TimeSpan AppendBatchingDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets how many append requests a batch must already hold before <see cref="AppendBatchingDelay"/>
    /// applies. The default is 0.
    /// </summary>
    public int AppendBatchingDelayMinCount { get; set; }

    /// <summary>
    /// Gets or sets the number of events fetched per round trip when reading. The default is 1,000.
    /// </summary>
    public int ReadPageSize { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets the options for the tail: the read of the event log that the store's subscriptions share once they
    /// have caught up.
    /// </summary>
    public EventLogTailOptions Tail { get; set; } = EventLogTailOptions.Default;

    /// <summary>
    /// Gets or sets a value that indicates whether subscriptions require a synchronous standby. The default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Subscriptions are only given events that the synchronous standbys in <c>synchronous_standby_names</c> have
    /// flushed, so that a failover cannot undo them. On a single server that setting is empty, and a local flush is
    /// enough. In a replicated deployment an empty setting means that synchronous replication is off, and setting this
    /// makes subscriptions report it as an error and stop delivering events until it is configured.
    /// </remarks>
    public bool RequireSynchronousStandby { get; set; }
}