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
        public required long StartGlobalPosition { get; set; }
        public required long EndGlobalPosition { get; set; }
        public required DateTime CommittedAtUtc { get; set; }
        public required List<EventDocument> Events { get; init; }
    }

    public sealed class EventDocument
    {
        public required string EventName { get; init; }
        public required BsonValue EventData { get; init; }
        public required BsonValue Metadata { get; init; }
    }
}