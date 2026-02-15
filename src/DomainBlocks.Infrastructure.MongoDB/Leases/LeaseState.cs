using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseState
{
    [BsonId]
    public required string ResourceId { get; init; }

    public required string HolderId { get; init; }

    public required long Epoch { get; init; }

    public required long NextSequence { get; init; }

    public required int ContentionPriority { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public required DateTime HeldSinceUtc { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }

    public required BsonDocument Data { get; init; }

    [BsonIgnore]
    public LeaseClaim Claim => new(ResourceId, HolderId, Epoch);
}