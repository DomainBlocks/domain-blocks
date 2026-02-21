using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.Infrastructure.MongoDB.Leases.Schema;

public sealed class LeaseState
{
    [BsonId]
    public required string ResourceId { get; init; }

    [BsonElement(LeaseStateFieldNames.HolderId)]
    public required string HolderId { get; init; }

    [BsonElement(LeaseStateFieldNames.Epoch)]
    public required long Epoch { get; init; }

    [BsonElement(LeaseStateFieldNames.ContentionPriority)]
    public required int ContentionPriority { get; init; }

    [BsonElement(LeaseStateFieldNames.HeldSinceUtc)]
    public required DateTime HeldSinceUtc { get; init; }

    [BsonElement(LeaseStateFieldNames.ExpiresAtUtc)]
    public required DateTime ExpiresAtUtc { get; init; }

    [BsonElement(LeaseStateFieldNames.Counters)]
    public Dictionary<string, long> Counters { get; init; } = [];

    [BsonElement(LeaseStateFieldNames.LastMutation)]
    public required LeaseStateMutation LastMutation { get; init; }

    [BsonIgnore]
    public LeaseClaim Claim => new(ResourceId, HolderId, Epoch);
}