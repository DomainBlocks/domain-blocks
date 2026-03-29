using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public sealed class AppendBatchCompleted
{
    [BsonElement(FieldNames.AppendedCommitIds)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required IReadOnlyCollection<Guid> Appends { get; init; }

    [BsonElement(FieldNames.DuplicateCommitIds)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required IReadOnlyCollection<Guid> Duplicates { get; init; }

    [BsonElement(FieldNames.Rejections)]
    public required IReadOnlyCollection<CommitRejection> Rejections { get; init; }

    public static class FieldNames
    {
        public const string AppendedCommitIds = "appends";
        public const string DuplicateCommitIds = "duplicates";
        public const string Rejections = "rejections";
    }
}