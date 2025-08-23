using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public class EventDocument<TPayload>
{
    public ObjectId Id { get; init; }
    public required string StreamId { get; init; }
    public required long StreamVersion { get; init; }
    public required string EventName { get; init; }
    public required IDictionary<string, string> Metadata { get; init; }
    public required DateTime CommittedAt { get; init; }
    public required TPayload Payload { get; init; }
}