using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.Coordination.MongoDB.Leases;

public sealed class LeaseState
{
    [BsonId]
    public required string ResourceId { get; init; }

    public required string HolderId { get; init; }

    public required long Epoch { get; set; }

    public required int ContentionPriority { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public required DateTime HeldSinceUtc { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }
}