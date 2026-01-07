using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class EventCollectionOptions
{
    public static readonly EventCollectionOptions<EventDocument> Default = new()
    {
        CollectionNamespace = new CollectionNamespace("domainblocks", "events"),
        DocumentSchema = EventDocumentSchema.Default
    };
}

public sealed class EventCollectionOptions<TEventDocument>
{
    public required CollectionNamespace CollectionNamespace { get; init; }
    public required EventDocumentSchema<TEventDocument> DocumentSchema { get; init; }
}