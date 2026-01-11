using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public class MongoEventStoreClientOptions<TEvent> where TEvent : notnull
{
    public required EventStoreCollectionOptions CollectionOptions { get; init; }
    public required IEventCodec<TEvent, BsonValue, BsonValue> EventCodec { get; init; }
}