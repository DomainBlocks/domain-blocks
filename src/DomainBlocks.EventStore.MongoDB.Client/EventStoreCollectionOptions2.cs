namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class EventStoreCollectionOptions2
{
    public static readonly EventStoreCollectionOptions2 Default = new()
    {
        DatabaseName = "domainblocks",
        AppendRequestsCollectionName = "dbx.append_requests",
        LoggedEventsCollectionName = "dbx.logged_events"
    };

    public required string DatabaseName { get; init; }
    public required string AppendRequestsCollectionName { get; init; }
    public required string LoggedEventsCollectionName { get; init; }
}