using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
internal sealed class AppendRequest
{
    public ObjectId Id { get; init; }

    [BsonElement(FieldNames.CommitId)]
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

    public static class FieldNames
    {
        public const string CommitId = "commitId";
        public const string StreamId = "streamId";
        public const string ExpectedStreamState = "expectedStreamState";
        public const string Events = "events";
        public const string CreatedAtUtc = "createdAtUtc";
    }
}