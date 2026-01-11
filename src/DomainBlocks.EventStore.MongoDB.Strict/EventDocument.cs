using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public sealed class EventDocument
{
    public required string EventName { get; init; }
    public required BsonValue EventData { get; init; }
    public required BsonValue Metadata { get; init; }
}