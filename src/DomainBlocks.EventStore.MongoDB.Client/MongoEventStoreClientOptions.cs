using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClientOptions<TEvent> where TEvent : notnull
{
    public required EventStoreCollectionOptions CollectionOptions { get; init; }
    public required IEventCodec<TEvent, byte[], byte[]> EventCodec { get; init; }
}