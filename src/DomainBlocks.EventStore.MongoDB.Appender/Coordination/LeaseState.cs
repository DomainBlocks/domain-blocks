using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public sealed class LeaseState
{
    [BsonId]
    public required string ResourceId { get; init; }

    public required string HolderId { get; init; }

    public required int HolderPriority { get; init; }

    public required long Epoch { get; set; }

    public required DateTime UpdatedAtUtc { get; init; }

    public required DateTime HeldSinceUtc { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }
}