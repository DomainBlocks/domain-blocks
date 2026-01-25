using System.ComponentModel.DataAnnotations;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

public sealed class MongoOptions
{
    [Required]
    public string DatabaseName { get; init; } = "domainblocks";

    [Required]
    public string SequencesCollectionName { get; init; } = "es_sequences";

    [Required]
    public string StreamCommitsCollectionName { get; init; } = "es_stream_commits";
}