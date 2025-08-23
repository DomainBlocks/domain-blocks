using System.Linq.Expressions;

namespace DomainBlocks.Persistence.MongoDB.Events;

public class MongoEventStoreOptions<TEventDocument, TPayload>
{
    public required IEventDocumentMapper<TEventDocument, TPayload> DocumentMapper { get; init; }
    public required Expression<Func<TEventDocument, string>> StreamIdSelector { get; init; }
    public required Expression<Func<TEventDocument, long>> StreamVersionSelector { get; init; }
}