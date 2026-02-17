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

    [BsonElement("streamId")]
    public required string StreamId { get; init; }

    [BsonElement("expectedStreamState")]
    [BsonSerializer(typeof(ExpectedStreamStateBsonSerializer))]
    public required ExpectedStreamState ExpectedStreamState { get; init; }

    [BsonElement("events")]
    public required Event[] Events { get; init; }

    [BsonElement("createdAtUtc")]
    public required DateTime CreatedAtUtc { get; init; }

    [BsonElement("lastSeenAtUtc")]
    public required DateTime LastSeenAtUtc { get; init; }

    public sealed class Event
    {
        [BsonElement("eventName")]
        public required string EventName { get; init; }

        [BsonElement("eventData")]
        public required BsonValue EventData { get; init; }

        [BsonElement("metadata")]
        public required BsonValue Metadata { get; init; }
    }
}