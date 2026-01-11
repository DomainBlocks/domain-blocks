using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public sealed class StreamCommit
{
    public ObjectId Id { get; init; }
    public required string StreamId { get; init; }
    public required long StartStreamVersion { get; init; }
    public required long StartGlobalPosition { get; init; }
    public required DateTime CommittedAtUtc { get; init; }
    public required EventDocument[] Events { get; init; }

    [BsonIgnore]
    public long EndStreamVersion => StartStreamVersion + Events.Length - 1;
}