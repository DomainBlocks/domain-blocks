using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
public sealed class EventLogEntry
{
    [BsonId]
    public required long Position { get; init; }

    [BsonElement(FieldNames.Epoch)]
    public required long Epoch { get; init; }

    [BsonElement(FieldNames.StreamId)]
    public required string StreamId { get; init; }

    [BsonElement(FieldNames.StreamVersion)]
    public required long StreamVersion { get; init; }

    [BsonElement(FieldNames.CommitId)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CommitId { get; init; }

    [BsonElement(FieldNames.CommitIndex)]
    public required int CommitIndex { get; init; }

    [BsonElement(FieldNames.EventName)]
    public required string EventName { get; init; }

    [BsonElement(FieldNames.EventData)]
    public required BsonValue EventData { get; init; }

    [BsonElement(FieldNames.Metadata)]
    public required BsonValue Metadata { get; init; }

    [BsonElement(FieldNames.WrittenAtUtc)]
    public required DateTime WrittenAtUtc { get; init; }

    public static class FieldNames
    {
        public const string Epoch = "epoch";
        public const string StreamId = "streamId";
        public const string StreamVersion = "streamVersion";
        public const string CommitId = "commitId";
        public const string CommitIndex = "commitIndex";
        public const string EventName = "eventName";
        public const string EventData = "eventData";
        public const string Metadata = "metadata";
        public const string WrittenAtUtc = "writtenAtUtc";
    }
}