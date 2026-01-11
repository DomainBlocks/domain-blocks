namespace DomainBlocks.EventStore.MongoDB.Strict;

public class MongoEventStoreClientOptions<TEvent, TEventDocument> where TEvent : notnull
{
    public required EventStoreCollectionOptions CollectionOptions { get; init; }
    public required EventDocumentSchema<TEventDocument> EventDocumentSchema { get; init; }
    public required IEventDocumentCodec<TEvent, TEventDocument> EventDocumentCodec { get; init; }
}