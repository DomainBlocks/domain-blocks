using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.Infrastructure.MongoDB.Leases.Schema;

public class LeaseStateMutation
{
    [BsonElement(LeaseStateMutationFieldNames.MutationKind)]
    [BsonRepresentation(BsonType.String)]
    public required LeaseStateMutationKind MutationKind { get; init; }

    [BsonElement(LeaseStateMutationFieldNames.MutatedAtUtc)]
    public required DateTime MutatedAtUtc { get; init; }
}