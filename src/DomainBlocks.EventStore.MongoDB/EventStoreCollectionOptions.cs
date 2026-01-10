namespace DomainBlocks.EventStore.MongoDB;

public sealed class EventStoreCollectionOptions
{
    public static readonly EventStoreCollectionOptions Default = new()
    {
        DatabaseName = "domainblocks",
        EventsCollectionName = "events",
        StreamCommitsCollectionName = "stream_commits"
    };

    public required string DatabaseName { get; init; }
    public required string EventsCollectionName { get; init; }
    public required string StreamCommitsCollectionName { get; init; }
}