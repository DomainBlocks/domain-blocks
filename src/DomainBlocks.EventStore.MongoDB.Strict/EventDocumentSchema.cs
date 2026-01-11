using System.Linq.Expressions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public static class EventDocumentSchema
{
    public static readonly EventDocumentSchema<EventDocument> Default = new()
    {
        StreamId = doc => doc.StreamId,
        StreamVersion = doc => doc.StreamVersion,
        CommitId = doc => doc.CommitId,
        CreatedAtUtc = doc => doc.CreatedAtUtc
    };
}

public sealed class EventDocumentSchema<TEventDocument>
{
    public required Expression<Func<TEventDocument, string>> StreamId { get; init; }
    public required Expression<Func<TEventDocument, long>> StreamVersion { get; init; }
    public required Expression<Func<TEventDocument, Guid>> CommitId { get; init; }
    public required Expression<Func<TEventDocument, DateTime>> CreatedAtUtc { get; init; }

    internal FieldDefinition<TEventDocument, string> StreamIdField =>
        new ExpressionFieldDefinition<TEventDocument, string>(StreamId);

    internal FieldDefinition<TEventDocument, long> StreamVersionField =>
        new ExpressionFieldDefinition<TEventDocument, long>(StreamVersion);

    internal FieldDefinition<TEventDocument, Guid> CommitIdField =>
        new ExpressionFieldDefinition<TEventDocument, Guid>(CommitId);

    internal FieldDefinition<TEventDocument, DateTime> CreatedAtUtcField =>
        new ExpressionFieldDefinition<TEventDocument, DateTime>(CreatedAtUtc);
}