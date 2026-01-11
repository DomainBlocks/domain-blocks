namespace DomainBlocks.EventStore.MongoDB.Strict;

public class MongoEventStoreClientOptions<TEvent> where TEvent : notnull
{
    public required EventStoreCollectionOptions CollectionOptions { get; init; }
    public required IEventDocumentCodec<TEvent> EventDocumentCodec { get; init; }
}