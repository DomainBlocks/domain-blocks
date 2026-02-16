using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public sealed class AppendRequest
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CommitId { get; init; }

    public required string StreamId { get; init; }

    [BsonSerializer(typeof(ExpectedStreamStateBsonSerializer))]
    public required ExpectedStreamState ExpectedStreamState { get; init; }

    public required Event[] Events { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime LastSeenAtUtc { get; init; }

    public sealed class Event
    {
        public required string EventName { get; init; }
        public required BsonValue EventData { get; init; }
        public required BsonValue Metadata { get; init; }
    }
}