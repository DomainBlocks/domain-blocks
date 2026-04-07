using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public record EventLogWriteResult(long StartPosition, long Count, BsonValue AppendBatchRecorded)
{
    public static readonly EventLogWriteResult Empty = new(0, 0, new BsonDocument());
}