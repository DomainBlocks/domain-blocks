using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client;

public static class Schema
{
    public sealed class StreamCommit
    {
        public ObjectId Id { get; init; }
        public required string StreamId { get; init; }
        public required long StartStreamVersion { get; init; }
        public required long EndStreamVersion { get; init; }
        public required long StartGlobalPosition { get; init; }
        public required long EndGlobalPosition { get; init; }
        public required DateTime CommittedAtUtc { get; init; }
        public required EventDocument[] Events { get; init; }
    }

    public sealed class EventDocument
    {
        public required string EventName { get; init; }
        public required BsonValue EventData { get; init; }
        public required BsonValue Metadata { get; init; }
    }

    public sealed class EventDocument2
    {
        [BsonId]
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public required Guid EventId { get; init; }

        public required Guid CommitId { get; init; }

        public required string StreamId { get; init; }

        public required string EventName { get; init; }

        public required BsonValue EventData { get; init; }

        public required BsonValue Metadata { get; init; }
    }
}