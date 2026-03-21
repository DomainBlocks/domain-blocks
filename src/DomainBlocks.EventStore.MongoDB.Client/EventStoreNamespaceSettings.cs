using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed record EventStoreNamespaceSettings
{
    public static readonly EventStoreNamespaceSettings Default = new()
    {
        DatabaseName = "domainblocks",
        AppendRequestsCollectionName = "dbx_append_requests",
        EventLogCollectionName = "dbx_event_log",
        LeasesCollectionName = "dbx_leases"
    };

    public required string DatabaseName { get; init; }
    public required string AppendRequestsCollectionName { get; init; }
    public required string EventLogCollectionName { get; init; }
    public required string LeasesCollectionName { get; init; }

    public CollectionNamespace AppendRequestsCollectionNamespace => new(DatabaseName, AppendRequestsCollectionName);
    public CollectionNamespace EventLogCollectionNamespace => new(DatabaseName, EventLogCollectionName);
    public CollectionNamespace LeasesCollectionNamespace => new(DatabaseName, LeasesCollectionName);
}