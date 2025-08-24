using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreOptions
{
    public static MongoEventStoreOptions<EventDocument<BsonDocument>, BsonDocument> CreateDefault(
        string collectionName = "events")
    {
        return CreateDefault<BsonDocument>();
    }

    public static MongoEventStoreOptions<EventDocument<TPayload>, TPayload> CreateDefault<TPayload>(
        string collectionName = "events")
    {
        return new MongoEventStoreOptions<EventDocument<TPayload>, TPayload>
        {
            CollectionName = collectionName,
            DocumentMapper = new EventDocumentMapper<TPayload>(),
            StreamIdSelector = doc => doc.StreamId,
            StreamVersionSelector = doc => doc.StreamVersion,
            CommittedAtSelector = doc => doc.CommittedAt
        };
    }
}

public class MongoEventStoreOptions<TEventDocument, TPayload>
{
    public required string CollectionName { get; init; }
    public required IEventDocumentMapper<TEventDocument, TPayload> DocumentMapper { get; init; }
    public required Expression<Func<TEventDocument, string>> StreamIdSelector { get; init; }
    public required Expression<Func<TEventDocument, long>> StreamVersionSelector { get; init; }
    public required Expression<Func<TEventDocument, DateTime>> CommittedAtSelector { get; init; }

    internal FieldDefinition<TEventDocument, string> StreamIdField =>
        new ExpressionFieldDefinition<TEventDocument, string>(StreamIdSelector);

    internal FieldDefinition<TEventDocument, long> StreamVersionField =>
        new ExpressionFieldDefinition<TEventDocument, long>(StreamVersionSelector);

    internal FieldDefinition<TEventDocument, DateTime> CommittedAtField =>
        new ExpressionFieldDefinition<TEventDocument, DateTime>(CommittedAtSelector);
}