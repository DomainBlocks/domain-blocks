namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class MongoEventStoreOptions
{
    public string DatabaseName { get; set; } = "domainblocks";

    public string AppendRequestsCollectionName { get; set; } = "dbx_append_requests";

    public string EventLogCollectionName { get; set; } = "dbx_event_log";

    public string LeasesCollectionName { get; set; } = "dbx_leases";

    /// <summary>
    /// Gets or sets how long an append request document survives in MongoDB before being automatically deleted.
    /// </summary>
    public TimeSpan AppendRequestTtl { get; set; } = TimeSpan.FromSeconds(120);

    public int RequestQueueCapacity { get; set; } = 1000;

    public int RequestInsertBatchSize { get; set; } = 1000;

    public LeaderOptions Leader { get; set; } = new();
}