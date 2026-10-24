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
    /// Gets or sets the options for the replication connection that feeds live subscriptions.
    /// </summary>
    public PostgresReplicationOptions Replication { get; set; } = new();
}