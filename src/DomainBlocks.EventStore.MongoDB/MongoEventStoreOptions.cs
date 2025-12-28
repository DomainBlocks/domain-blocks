using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreOptions
{
    private const string DefaultEventCollectionName = "domainblocks.events";

    public static MongoEventStoreOptions<DefaultEventDocument<BsonValue>, BsonValue> CreateDefault(
        string eventCollectionName = DefaultEventCollectionName)
    {
        return CreateDefault<BsonValue>(eventCollectionName);
    }

    public static MongoEventStoreOptions<DefaultEventDocument<TSerialized>, TSerialized> CreateDefault<TSerialized>(
        string eventCollectionName = DefaultEventCollectionName)
        where TSerialized : notnull
    {
        return new MongoEventStoreOptions<DefaultEventDocument<TSerialized>, TSerialized>
        {
            EventCollectionName = eventCollectionName,
            EventDocumentConverter = new DefaultEventDocumentConverter<TSerialized>(),
            StreamIdExpression = doc => doc.StreamId,
            StreamVersionExpression = doc => doc.StreamVersion,
            CreatedAtExpression = doc => doc.CreatedAt
        };
    }
}

public class MongoEventStoreOptions<TEventDocument, TSerialized> where TSerialized : notnull
{
    public required string EventCollectionName { get; init; }
    public required IEventDocumentConverter<TEventDocument, TSerialized> EventDocumentConverter { get; init; }
    public required Expression<Func<TEventDocument, string>> StreamIdExpression { get; init; }
    public required Expression<Func<TEventDocument, ulong>> StreamVersionExpression { get; init; }
    public required Expression<Func<TEventDocument, DateTime>> CreatedAtExpression { get; init; }

    internal FieldDefinition<TEventDocument, string> StreamIdField =>
        new ExpressionFieldDefinition<TEventDocument, string>(StreamIdExpression);

    internal FieldDefinition<TEventDocument, ulong> StreamVersionField =>
        new ExpressionFieldDefinition<TEventDocument, ulong>(StreamVersionExpression);

    internal FieldDefinition<TEventDocument, DateTime> CreatedAtField =>
        new ExpressionFieldDefinition<TEventDocument, DateTime>(CreatedAtExpression);
}