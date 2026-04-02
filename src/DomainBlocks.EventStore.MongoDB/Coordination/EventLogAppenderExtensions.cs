using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public static class EventLogAppenderExtensions
{
    extension(IEventLogAppender appender)
    {
        public Task<AppendBatchResult> AppendBatchAsync(
            IEnumerable<BsonDocument> requests,
            CancellationToken cancellationToken)

        {
            appender.StartPrefetch(requests, cancellationToken);
            return appender.FlushAsync(cancellationToken);
        }
    }
}