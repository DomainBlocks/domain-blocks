using System.Linq.Expressions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class EventDocumentMap<TEventDocument>
{
    public required Expression<Func<TEventDocument, string>> StreamId { get; init; }
    public required Expression<Func<TEventDocument, ulong>> StreamVersion { get; init; }
    public required Expression<Func<TEventDocument, DateTime>> CreatedAt { get; init; }

    internal FieldDefinition<TEventDocument, string> StreamIdField =>
        new ExpressionFieldDefinition<TEventDocument, string>(StreamId);

    internal FieldDefinition<TEventDocument, ulong> StreamVersionField =>
        new ExpressionFieldDefinition<TEventDocument, ulong>(StreamVersion);

    internal FieldDefinition<TEventDocument, DateTime> CreatedAtField =>
        new ExpressionFieldDefinition<TEventDocument, DateTime>(CreatedAt);
}