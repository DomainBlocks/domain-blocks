using DomainBlocks.EventStore.MongoDB.Coordination;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Coordination;

internal static class EventLogWriterExtensions
{
    extension(IEventLogWriter writer)
    {
        public Task<EventLogWriteResult> WriteAsync(
            IEnumerable<BsonDocument> requests,
            CancellationToken cancellationToken)

        {
            writer.Prepare(requests, cancellationToken);
            return writer.FlushAsync(cancellationToken);
        }
    }
}