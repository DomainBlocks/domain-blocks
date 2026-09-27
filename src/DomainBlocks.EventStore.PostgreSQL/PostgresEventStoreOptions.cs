using DomainBlocks.EventStore.Filtering;

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

    /// <summary>
    /// Configures the logical replication connection that feeds live subscriptions.
    /// </summary>
    public PostgresReplicationOptions Replication { get; set; } = new();

    /// <summary>
    /// Selects the events that the store's subscriptions are ever given, for an application that knows them up front.
    /// It becomes the row filter of the store's publication, so the server leaves the rest out of the live feed, which
    /// saves their traffic as well. Every subscription is subject to it, along with its own filter. Reads are not. The
    /// default is every event.
    /// </summary>
    /// <remarks>
    /// It needs PostgreSQL 15 or later, and has to be a filter that the database can evaluate the whole of as it
    /// stands: by event name rather than by type, as a publication is named after the filter alone, by whoever
    /// initializes the schema, who has no codec. The publication is named after the filter, so stores with different
    /// filters can share a schema, as they do while a deployment rolls out. With a filter, keep the schema name to 40
    /// characters, as PostgreSQL cuts a longer name short.
    /// </remarks>
    public EventFilter SubscriptionFilter
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = EventFilter.All;
}