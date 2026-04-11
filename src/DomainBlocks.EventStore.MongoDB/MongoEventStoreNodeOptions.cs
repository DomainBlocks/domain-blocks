namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreNodeOptions
{
    public string DatabaseName { get; set; } = "domainblocks";

    public string RequestsCollectionName { get; set; } = "dbx_requests";

    public string EventLogCollectionName { get; set; } = "dbx_event_log";

    public string LeasesCollectionName { get; set; } = "dbx_leases";

    /// <summary>
    /// Gets or sets how long request documents survives in MongoDB before being automatically deleted.
    /// </summary>
    public TimeSpan RequestDocumentTtl { get; set; } = TimeSpan.FromSeconds(120);

    public NodeRole NodeRole { get; set; } = NodeRole.ClientLeader;

    public int RequestQueueCapacity { get; set; } = 1_000;

    public int RequestBatchSize { get; set; } = 500;

    public int WriteQueueCapacity { get; set; } = 1_000;

    public int WriteBatchSize { get; set; } = 500;
}