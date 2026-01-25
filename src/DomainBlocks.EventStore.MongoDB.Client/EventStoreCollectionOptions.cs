namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class EventStoreCollectionOptions
{
    public static readonly EventStoreCollectionOptions Default = new()
    {
        DatabaseName = "domainblocks",
        StreamCommitsCollectionName = "es_stream_commits",
        SequencesCollectionName = "es_sequences"
    };

    public required string DatabaseName { get; init; }
    public required string StreamCommitsCollectionName { get; init; }
    public required string SequencesCollectionName { get; init; }
}