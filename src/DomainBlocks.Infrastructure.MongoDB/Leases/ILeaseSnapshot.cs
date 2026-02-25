using MongoDB.Bson;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseSnapshot
{
    string ResourceId { get; }
    string HolderId { get; }
    long Epoch { get; }
    int ContentionPriority { get; }
    DateTimeOffset HeldSince { get; }
    DateTimeOffset ExpiresAt { get; }
    BsonDocument State { get; }
    DateTimeOffset LastUpdatedAt { get; }
    LeaseUpdateKind LastUpdateKind { get; }
}