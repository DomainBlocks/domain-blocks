using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseDocument
{
    [BsonId]
    public required string ResourceId { get; init; }

    [BsonElement(FieldNames.HolderId)]
    public required string HolderId { get; init; }

    [BsonElement(FieldNames.Epoch)]
    public required long Epoch { get; init; }

    [BsonElement(FieldNames.ContentionPriority)]
    public required int ContentionPriority { get; init; }

    [BsonElement(FieldNames.HeldSinceUtc)]
    public required DateTime HeldSinceUtc { get; init; }

    [BsonElement(FieldNames.ExpiresAtUtc)]
    public required DateTime ExpiresAtUtc { get; init; }

    [BsonElement(FieldNames.State)]
    public required BsonDocument State { get; init; }

    [BsonElement(FieldNames.LastUpdatedAtUtc)]
    public required DateTime LastUpdatedAtUtc { get; init; }

    [BsonElement(FieldNames.LastUpdateKind)]
    [BsonRepresentation(BsonType.String)]
    public required LeaseUpdateKind LastUpdateKind { get; init; }

    [BsonIgnore]
    public LeaseClaim Claim => new(ResourceId, HolderId, Epoch);

    public static class FieldNames
    {
        public const string HolderId = "holderId";
        public const string Epoch = "epoch";
        public const string ContentionPriority = "contentionPriority";
        public const string HeldSinceUtc = "heldSinceUtc";
        public const string ExpiresAtUtc = "expiresAtUtc";
        public const string State = "state";
        public const string LastUpdatedAtUtc = "lastUpdatedAtUtc";
        public const string LastUpdateKind = "lastUpdateKind";
    }
}