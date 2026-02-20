using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class EventStoreNamespaceOptions
{
    public static readonly EventStoreNamespaceOptions Default = new()
    {
        DatabaseName = "domainblocks",
        AppendRequestsCollectionName = "dbx.append_requests",
        LoggedEventsCollectionName = "dbx.logged_events",
        LeasesCollectionName = "dbx.leases"
    };

    public required string DatabaseName { get; init; }
    public required string AppendRequestsCollectionName { get; init; }
    public required string LoggedEventsCollectionName { get; init; }
    public required string LeasesCollectionName { get; init; }

    public CollectionNamespace AppendRequestsCollectionNamespace => new(DatabaseName, AppendRequestsCollectionName);
    public CollectionNamespace LoggedEventsCollectionNamespace => new(DatabaseName, LoggedEventsCollectionName);
    public CollectionNamespace LeasesCollectionNamespace => new(DatabaseName, LeasesCollectionName);
}