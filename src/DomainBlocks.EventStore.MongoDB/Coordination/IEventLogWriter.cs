using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public interface IEventLogWriter
{
    void Prepare(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken);

    Task<EventLogWriteResult> FlushAsync(CancellationToken cancellationToken);
}