using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
internal sealed class LeaseUpdate
{
    [BsonElement(FieldNames.Kind)]
    [BsonRepresentation(BsonType.String)]
    public required LeaseUpdateKind Kind { get; init; }

    [BsonElement(FieldNames.AtUtc)]
    public required DateTime AtUtc { get; init; }

    public static class FieldNames
    {
        public const string Kind = "kind";
        public const string AtUtc = "atUtc";
    }
}