using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
public sealed class ConflictsRejected
{
    [BsonElement(FieldNames.Conflicts)]
    public required IReadOnlyCollection<AppendConflict> Conflicts { get; init; }

    public static class FieldNames
    {
        public const string Conflicts = "conflicts";
    }
}