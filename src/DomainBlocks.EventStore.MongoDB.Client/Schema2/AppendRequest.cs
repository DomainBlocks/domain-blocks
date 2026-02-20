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

    [BsonElement(AppendRequestFieldNames.StreamId)]
    public required string StreamId { get; init; }

    [BsonElement(AppendRequestFieldNames.ExpectedStreamState)]
    [BsonSerializer(typeof(ExpectedStreamStateBsonSerializer))]
    public required ExpectedStreamState ExpectedStreamState { get; init; }

    [BsonElement(AppendRequestFieldNames.Events)]
    public required Event[] Events { get; init; }

    [BsonElement(AppendRequestFieldNames.CreatedAtUtc)]
    public required DateTime CreatedAtUtc { get; init; }

    [BsonElement(AppendRequestFieldNames.LastSeenAtUtc)]
    public required DateTime LastSeenAtUtc { get; init; }

    public sealed class Event
    {
        [BsonElement(AppendRequestFieldNames.Event.EventName)]
        public required string EventName { get; init; }

        [BsonElement(AppendRequestFieldNames.Event.EventData)]
        public required BsonValue EventData { get; init; }

        [BsonElement(AppendRequestFieldNames.Event.Metadata)]
        public required BsonValue Metadata { get; init; }
    }
}