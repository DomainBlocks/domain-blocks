using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class EventCollectionOptions
{
    public static readonly EventCollectionOptions<EventDocument> Default = new()
    {
        CollectionNamespace = new CollectionNamespace("domainblocks", "events"),
        DocumentSchema = new EventDocumentSchema<EventDocument>
        {
            StreamId = doc => doc.StreamId,
            StreamVersion = doc => doc.StreamVersion,
            CreatedAtUtc = doc => doc.CreatedAtUtc
        }
    };
}

public sealed class EventCollectionOptions<TEventDocument>
{
    public required CollectionNamespace CollectionNamespace { get; init; }
    public required EventDocumentSchema<TEventDocument> DocumentSchema { get; init; }
}