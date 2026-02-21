using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class MongoEventStoreClientOptions2<TEvent> where TEvent : notnull
{
    public required EventStoreNamespaceSettings NamespaceSettings { get; init; }
    public required EventCodec<TEvent, BsonValue, BsonValue> EventCodec { get; init; }
}