using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client;

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