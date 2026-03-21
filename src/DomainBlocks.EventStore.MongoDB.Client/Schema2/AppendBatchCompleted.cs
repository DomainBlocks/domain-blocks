using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema2;

public sealed class AppendBatchCompleted
{
    [BsonElement(FieldNames.AppendedCommitIds)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required IReadOnlyList<Guid> AppendedCommitIds { get; init; }

    [BsonElement(FieldNames.RejectedCommitIds)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required IReadOnlyList<Guid> RejectedCommitIds { get; init; }

    [BsonElement(FieldNames.DuplicateCommitIds)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required IReadOnlyList<Guid> DuplicateCommitIds { get; init; }

    public static class FieldNames
    {
        public const string AppendedCommitIds = "appendedCommitIds";
        public const string RejectedCommitIds = "rejectedCommitIds";
        public const string DuplicateCommitIds = "duplicateCommitIds";
    }
}