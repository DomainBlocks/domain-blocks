using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema2;

public sealed class AppendRequest
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CommitId { get; init; }

    [BsonElement(FieldNames.StreamId)]
    public required string StreamId { get; init; }

    [BsonElement(FieldNames.ExpectedStreamState)]
    [BsonSerializer(typeof(ExpectedStreamStateBsonSerializer))]
    public required ExpectedStreamState ExpectedStreamState { get; init; }

    [BsonElement(FieldNames.Events)]
    public required PendingEvent[] Events { get; init; }

    [BsonElement(FieldNames.CreatedAtUtc)]
    public required DateTime CreatedAtUtc { get; init; }

    [BsonElement(FieldNames.LastSeenAtUtc)]
    public required DateTime LastSeenAtUtc { get; init; }

    [BsonElement(FieldNames.CompletedAtUtc)]
    [BsonIgnoreIfNull]
    public DateTime? CompletedAtUtc { get; init; }

    public static class FieldNames
    {
        public const string StreamId = "streamId";
        public const string ExpectedStreamState = "expectedStreamState";
        public const string Events = "events";
        public const string CreatedAtUtc = "createdAtUtc";
        public const string LastSeenAtUtc = "lastSeenAtUtc";
        public const string CompletedAtUtc = "completedAtUtc";
    }
}