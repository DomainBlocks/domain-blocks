using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
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
        public ObjectId Id { get; init; }

        public required string StreamId { get; init; }

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public required Guid CommitId { get; init; }

        public required int CommitIndex { get; init; }

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public required Guid EventId { get; init; }

        public required string EventName { get; init; }

        public required BsonValue EventData { get; init; }

        public required BsonValue Metadata { get; init; }

        public required DateTime CreatedAtUtc { get; init; }
    }

    public sealed class CommitProposed
    {
        public required int EventCount { get; init; }

        [BsonSerializer(typeof(ExpectedStreamStateBsonSerializer))]
        public required ExpectedStreamState ExpectedStreamState { get; init; }
    }

    public sealed class LeaderElected
    {
        public required string HolderId { get; init; }
        public required long Epoch { get; init; }
        public required long GlobalPosition { get; init; }
    }

    public sealed class CommitConfirmed
    {
        public required long Epoch { get; init; }
        public required long StartStreamVersion { get; init; }
        public required long StartGlobalPosition { get; init; }
        public required int EventCount { get; init; }
    }

    public sealed class CommitRejected
    {
        public required long Epoch { get; init; }
    }
}