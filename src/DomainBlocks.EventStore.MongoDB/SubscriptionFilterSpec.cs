using DomainBlocks.EventStore.Filtering;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal sealed class SubscriptionFilterSpec(
    FilterDefinition<BsonDocument> endPosition,
    FilterDefinition<BsonDocument> catchUp,
    EventFilter live)
{
    public FilterDefinition<BsonDocument> EndPosition { get; } = endPosition;
    public FilterDefinition<BsonDocument> CatchUp { get; } = catchUp;
    public EventFilter Live { get; } = live;

    public static SubscriptionFilterSpec ForAll(EventFilter? filter)
    {
        return new SubscriptionFilterSpec(
            FilterDefinition<BsonDocument>.Empty,
            filter is null ? FilterDefinition<BsonDocument>.Empty : MongoFilterTranslator.Translate(filter),
            filter ?? EventFilter.All);
    }

    public static SubscriptionFilterSpec ForStream(string streamId, EventFilter? filter)
    {
        var streamFilter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

        return new SubscriptionFilterSpec(
            streamFilter,
            filter is null ? streamFilter : streamFilter & MongoFilterTranslator.Translate(filter),
            filter is null ? EventFilter.StreamIds(streamId) : EventFilter.StreamIds(streamId) & filter);
    }
}