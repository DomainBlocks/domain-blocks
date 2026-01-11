using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public sealed class StreamCommit
{
    public ObjectId Id { get; init; }
    public required string StreamId { get; init; }
    public required long StartStreamVersion { get; init; }
    public required long StartGlobalPosition { get; init; }
    public required int EventCount { get; init; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CommitId { get; init; }

    public required DateTime CommittedAtUtc { get; init; }
}