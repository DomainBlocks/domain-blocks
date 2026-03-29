using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

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

    [BsonElement(FieldNames.EventName)]
    public required string EventName { get; init; }

    [BsonElement(FieldNames.EventData)]
    public required BsonValue EventData { get; init; }

    [BsonElement(FieldNames.Metadata)]
    public required BsonValue Metadata { get; init; }

    [BsonElement(FieldNames.WrittenAtUtc)]
    public required DateTime WrittenAtUtc { get; init; }
}