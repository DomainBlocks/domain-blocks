using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// The default event document for storing and retrieving events in Mongo.
/// </summary>
public class EventDocument
{
    public ObjectId Id { get; init; }
    public required string StreamId { get; init; }
    public required ulong StreamVersion { get; init; }
    public required string EventName { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required BsonValue EventData { get; init; }
    public required BsonValue Metadata { get; init; }
}