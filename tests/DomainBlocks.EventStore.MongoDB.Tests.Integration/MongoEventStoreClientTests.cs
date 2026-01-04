using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientTests : EventStoreClientTests
{
    protected override async Task<IEventStoreClient<IDomainEvent>> CreateClientAsync()
    {
        var codecOptions = new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = new EventTypeMapBuilder().MapType<TestEvent>().Build(),
            EventSerializer = new BsonDocumentSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        };

        var options = new MongoEventStoreClientOptions<IDomainEvent, EventDocument>
        {
            Collection = EventCollectionOptions.Default,
            DocumentCodec = EventDocumentCodec.Create(EventCodec.Create(codecOptions))
        };

        var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var collection = mongoClient.GetCollection<EventDocument>(options.Collection.CollectionNamespace);
        var eventStoreClient = new MongoEventStoreClient<IDomainEvent, EventDocument>(collection, options);

        await MongoEventStoreAdmin.EnsureIndexesAsync(collection, options.Collection);

        return eventStoreClient;
    }
}