namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class EventStoreCollectionOptions2
{
    public static readonly EventStoreCollectionOptions2 Default = new()
    {
        DatabaseName = "domainblocks",
        StreamCommitsCollectionName = "es_stream_commits",
        SequencesCollectionName = "es_sequences",

        // New
        EventsCollectionName = "dbx_events"
    };

    public required string DatabaseName { get; init; }
    public required string StreamCommitsCollectionName { get; init; }
    public required string SequencesCollectionName { get; init; }

    // New
    public required string EventsCollectionName { get; init; }
}