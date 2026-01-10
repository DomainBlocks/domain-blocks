using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class StreamCommit
{
    public ObjectId Id { get; init; }
    public required string StreamId { get; init; }
    public required ulong StartVersion { get; init; }
    public required ulong EventCount { get; init; }
    public required Guid CommitId { get; init; }
    public required DateTime CommittedAtUtc { get; init; }
}