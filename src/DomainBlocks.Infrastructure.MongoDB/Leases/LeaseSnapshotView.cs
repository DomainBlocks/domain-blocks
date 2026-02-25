using MongoDB.Bson;
using FieldNames = DomainBlocks.Infrastructure.MongoDB.Leases.LeaseDocument.FieldNames;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseSnapshotView(BsonDocument doc) : ILeaseSnapshot
{
    public string ResourceId => doc["_id"].AsString;
    public string HolderId => doc[FieldNames.HolderId].AsString;
    public long Epoch => doc[FieldNames.Epoch].ToInt64();
    public int ContentionPriority => doc[FieldNames.ContentionPriority].ToInt32();
    public DateTimeOffset HeldSince => doc[FieldNames.HeldSinceUtc].ToUniversalTime();
    public DateTimeOffset ExpiresAt => doc[FieldNames.ExpiresAtUtc].ToUniversalTime();

    public BsonDocument State => doc is RawBsonDocument raw
        ? raw[FieldNames.State].AsBsonDocument
        : doc[FieldNames.State].DeepClone().AsBsonDocument;

    public DateTimeOffset LastUpdatedAt => doc[FieldNames.LastUpdatedAtUtc].ToUniversalTime();
    public LeaseUpdateKind LastUpdateKind => Enum.Parse<LeaseUpdateKind>(doc[FieldNames.LastUpdateKind].AsString);
}