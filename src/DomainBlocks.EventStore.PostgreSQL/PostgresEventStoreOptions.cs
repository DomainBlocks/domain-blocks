namespace DomainBlocks.EventStore.PostgreSQL;

public sealed class PostgresEventStoreOptions
{
    /// <summary>
    /// The schema that holds the event log, sequence and append function. Must match ^[a-z_][a-z0-9_]{0,62}$.
    /// </summary>
    public string Schema { get; set; } = "dbx";

    /// <summary>
    /// The maximum number of append requests that may be queued before callers are made to wait.
    /// </summary>
    public int AppendQueueCapacity { get; set; } = 1_000;

    /// <summary>
    /// The maximum number of append requests committed together in one round trip to the database.
    /// </summary>
    public int AppendBatchSize { get; set; } = 500;

    /// <summary>
    /// How long to wait for further append requests to accumulate before committing a partial batch. Zero disables
    /// coalescing.
    /// </summary>
    public TimeSpan AppendBatchingDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// The number of queued append requests that must be observed together before the batching delay applies.
    /// </summary>
    public int AppendBatchingDelayMinCount { get; set; }

    /// <summary>
    /// The number of events fetched per round trip when reading. Reads page through the log with keyset queries so
    /// that a slow consumer does not hold a pooled connection open for the whole enumeration.
    /// </summary>
    public int ReadBatchSize { get; set; } = 1_000;
}
