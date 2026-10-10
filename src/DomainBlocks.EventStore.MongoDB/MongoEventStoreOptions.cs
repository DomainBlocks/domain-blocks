namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides options for a MongoDB event store.
/// </summary>
public sealed class MongoEventStoreOptions
{
    /// <summary>
    /// Gets or sets the name of the database that holds the event log and sequence collections. The default is
    /// <c>domainblocks</c>.
    /// </summary>
    public string DatabaseName { get; set; } = "domainblocks";

    /// <summary>
    /// Gets or sets the name of the collection that holds the event log. The default is <c>dbx_event_log</c>.
    /// </summary>
    public string EventLogCollectionName { get; set; } = "dbx_event_log";

    /// <summary>
    /// Gets or sets the name of the collection that holds the sequence document from which log positions are claimed.
    /// The default is <c>dbx_sequences</c>.
    /// </summary>
    public string SequencesCollectionName { get; set; } = "dbx_sequences";

    /// <summary>
    /// Gets or sets the maximum number of queued append requests before callers wait. The default is 1,000.
    /// </summary>
    public int AppendQueueCapacity { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets the maximum number of append requests committed in one transaction. The default is 500.
    /// </summary>
    public int AppendMaxBatchSize { get; set; } = 500;
}