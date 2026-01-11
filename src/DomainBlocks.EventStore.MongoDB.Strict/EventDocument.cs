using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Strict;

/// <summary>
/// The default event document for storing and retrieving events in Mongo.
/// </summary>
public sealed class EventDocument
{
    public ObjectId Id { get; init; }
    public required string StreamId { get; init; }
    public required long StreamVersion { get; init; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CommitId { get; init; }

    public required string EventName { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required BsonValue EventData { get; init; }
    public required BsonValue Metadata { get; init; }
}