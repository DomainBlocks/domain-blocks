using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

// We'll need a CommitDeclared system event as the idempotency gate, along with an ordered BulkWrite (?)
// Either use a special CommitIndex = -1, or partial index on 'CommitDeclared'.
// This may not need to be ordered to be correct, given this is above the HW mark.
public sealed class LoggedEvent
{
    [BsonId]
    public required long LogPosition { get; init; }

    public required long Epoch { get; init; }

    public required string StreamId { get; init; }

    public required long StreamVersion { get; init; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid CommitId { get; init; }

    public required int CommitIndex { get; init; }

    public required string EventName { get; init; }

    public required BsonValue EventData { get; init; }

    public required BsonValue Metadata { get; init; }

    public required DateTime WrittenAtUtc { get; init; }
}