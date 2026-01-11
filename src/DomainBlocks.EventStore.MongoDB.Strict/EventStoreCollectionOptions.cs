namespace DomainBlocks.EventStore.MongoDB.Strict;

public sealed class EventStoreCollectionOptions
{
    public static readonly EventStoreCollectionOptions Default = new()
    {
        DatabaseName = "domainblocks",
        EventsCollectionName = "es_events",
        StreamCommitsCollectionName = "es_stream_commits",
        SequencesCollectionName = "es_sequences"
    };

    public required string DatabaseName { get; init; }
    public required string EventsCollectionName { get; init; }
    public required string StreamCommitsCollectionName { get; init; }
    public required string SequencesCollectionName { get; init; }
}