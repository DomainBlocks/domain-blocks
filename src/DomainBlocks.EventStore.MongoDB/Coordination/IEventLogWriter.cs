using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal interface IEventLogWriter
{
    void Prepare(IEnumerable<BsonDocument> requests, CancellationToken cancellationToken);

    Task<EventLogWriteResult> FlushAsync(CancellationToken cancellationToken);
}