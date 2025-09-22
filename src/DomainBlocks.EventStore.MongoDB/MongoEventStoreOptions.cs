using System.Linq.Expressions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreOptions
{
    private const string DefaultEventCollectionName = "domainblocks.events";

    public static MongoEventStoreOptions<EventDocument> CreateDefault(
        string eventCollectionName = DefaultEventCollectionName)
    {
        return new MongoEventStoreOptions<EventDocument>
        {
            EventCollectionName = eventCollectionName,
            EventDocumentConverter = new EventDocumentConverter(),
            StreamIdExpression = doc => doc.StreamId,
            StreamVersionExpression = doc => doc.StreamVersion,
            CommittedAtExpression = doc => doc.CommittedAt
        };
    }
}

public class MongoEventStoreOptions<TEventDocument>
{
    public required string EventCollectionName { get; init; }
    public required IEventDocumentConverter<TEventDocument> EventDocumentConverter { get; init; }
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