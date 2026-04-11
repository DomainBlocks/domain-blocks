using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
internal sealed class DuplicatesSkipped
{
    [BsonElement(FieldNames.CommitIds)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required IReadOnlyCollection<Guid> CommitIds { get; init; }

    public static class FieldNames
    {
        public const string CommitIds = "commitIds";
    }
}