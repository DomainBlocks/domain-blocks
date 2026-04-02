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

    public ClientOptions Client { get; set; } = new();

    public LeaderOptions Leader { get; set; } = new();
}