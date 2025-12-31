using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreOptions
{
    private const string DefaultEventCollectionName = "domainblocks.events";

    public static MongoEventStoreOptions<DefaultEventDocument, BsonValue, BsonValue> CreateDefault(
        string eventCollectionName = DefaultEventCollectionName)
    {
        return new MongoEventStoreOptions<DefaultEventDocument, BsonValue, BsonValue>
        {
            EventCollectionName = eventCollectionName,
            EventDocumentConverter = new DefaultEventDocumentConverter(),
            StreamIdExpression = doc => doc.StreamId,
            StreamVersionExpression = doc => doc.StreamVersion,
            CreatedAtExpression = doc => doc.CreatedAt
        };
    }
}

public class MongoEventStoreOptions<TEventDocument, TEventData, TMetadata>
    where TEventData : notnull
    where TMetadata : notnull
{
    public required string EventCollectionName { get; init; }
    public required IEventDocumentConverter<TEventDocument, TEventData, TMetadata> EventDocumentConverter { get; init; }
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