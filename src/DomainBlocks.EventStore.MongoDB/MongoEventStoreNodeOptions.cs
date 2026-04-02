namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreNodeOptions
{
    public string DatabaseName { get; set; } = "domainblocks";

    public string RequestsCollectionName { get; set; } = "dbx_requests";

    public string EventLogCollectionName { get; set; } = "dbx_event_log";

    public string LeasesCollectionName { get; set; } = "dbx_leases";

    public NodeRole NodeRole { get; set; }

    public ClientOptions Client { get; set; } = new();

    public LeaderOptions Leader { get; set; } = new();
}