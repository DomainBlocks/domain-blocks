using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

public static class Schema
{
    public sealed class StreamCommit
    {
        public ObjectId Id { get; init; }
        public required string StreamId { get; init; }
        public required long StartStreamVersion { get; init; }
        public required long EndStreamVersion { get; init; }
        public long StartGlobalPosition { get; set; }
        public long EndGlobalPosition { get; set; }
        public DateTime CommittedAtUtc { get; set; }
        public required EventDocument[] Events { get; init; }
    }

    public sealed class EventDocument
    {
        public required string EventName { get; init; }
        public required BsonValue EventData { get; init; }
        public required BsonValue Metadata { get; init; }
    }
}