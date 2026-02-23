using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster;

public sealed class LogLeaseState
{
    [BsonElement(FieldNames.CommitPosition)]
    public required long CommitPosition { get; init; }

    public static class FieldNames
    {
        public const string CommitPosition = "commitPosition";
    }
}