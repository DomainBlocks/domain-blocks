using System.Linq.Expressions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class EventDocumentSchema
{
    public static readonly EventDocumentSchema<EventDocument> Default = new()
    {
        StreamId = doc => doc.StreamId,
        StreamVersion = doc => doc.StreamVersion,
        CreatedAtUtc = doc => doc.CreatedAtUtc
    };
}

public sealed class EventDocumentSchema<TEventDocument>
{
    public required Expression<Func<TEventDocument, string>> StreamId { get; init; }
    public required Expression<Func<TEventDocument, ulong>> StreamVersion { get; init; }
    public required Expression<Func<TEventDocument, DateTime>> CreatedAtUtc { get; init; }

    internal FieldDefinition<TEventDocument, string> StreamIdField =>
        new ExpressionFieldDefinition<TEventDocument, string>(StreamId);

    internal FieldDefinition<TEventDocument, ulong> StreamVersionField =>
        new ExpressionFieldDefinition<TEventDocument, ulong>(StreamVersion);

    internal FieldDefinition<TEventDocument, DateTime> CreatedAtUtcField =>
        new ExpressionFieldDefinition<TEventDocument, DateTime>(CreatedAtUtc);
}