using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
internal sealed class LeaseDocument
{
    public const string LeaseId = "dbx_event_log_lease";

    public string Id { get; private init; } = LeaseId;

    [BsonElement(FieldNames.HolderId)]
    public required string HolderId { get; init; }

    [BsonElement(FieldNames.Epoch)]
    public required long Epoch { get; init; }

    [BsonElement(FieldNames.AcquiredAtUtc)]
    public required DateTime AcquiredAtUtc { get; init; }

    [BsonElement(FieldNames.ExpiresAtUtc)]
    public required DateTime ExpiresAtUtc { get; init; }

    [BsonElement(FieldNames.CommitPosition)]
    public long CommitPosition { get; init; }

    [BsonElement(FieldNames.LastUpdatedAtUtc)]
    public required DateTime LastUpdatedAtUtc { get; init; }

    public static class FieldNames
    {
        public const string HolderId = "holderId";
        public const string Epoch = "epoch";
        public const string AcquiredAtUtc = "acquiredAtUtc";
        public const string ExpiresAtUtc = "expiresAtUtc";
        public const string CommitPosition = "commitPosition";
        public const string LastUpdatedAtUtc = "lastUpdatedAtUtc";
    }
}