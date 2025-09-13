using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreOptions
{
    public static MongoEventStoreOptions<EventDocument<BsonDocument>, BsonDocument> CreateDefault(
        string collectionName = "events")
    {
        return CreateDefault<BsonDocument>(collectionName);
    }

    public static MongoEventStoreOptions<EventDocument<TPayload>, TPayload> CreateDefault<TPayload>(
        string collectionName = "events")
    {
        return new MongoEventStoreOptions<EventDocument<TPayload>, TPayload>
        {
            CollectionName = collectionName,
            DocumentMapper = new EventDocumentMapper<TPayload>(),
            StreamIdExpression = doc => doc.StreamId,
            StreamVersionExpression = doc => doc.StreamVersion,
            CommittedAtExpression = doc => doc.CommittedAt
        };
    }
}

public class MongoEventStoreOptions<TEventDocument, TPayload>
{
    public required string CollectionName { get; init; }
    public required IEventDocumentMapper<TEventDocument, TPayload> DocumentMapper { get; init; }
    public required Expression<Func<TEventDocument, string>> StreamIdExpression { get; init; }
    public required Expression<Func<TEventDocument, long>> StreamVersionExpression { get; init; }
    public required Expression<Func<TEventDocument, DateTime>> CommittedAtExpression { get; init; }

    internal FieldDefinition<TEventDocument, string> StreamIdField =>
        new ExpressionFieldDefinition<TEventDocument, string>(StreamIdExpression);

    internal FieldDefinition<TEventDocument, long> StreamVersionField =>
        new ExpressionFieldDefinition<TEventDocument, long>(StreamVersionExpression);

    internal FieldDefinition<TEventDocument, DateTime> CommittedAtField =>
        new ExpressionFieldDefinition<TEventDocument, DateTime>(CommittedAtExpression);
}