using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Strict.Tests.Integration;

[TestFixture]
public class StrictMongoEventStoreClientTests : EventStoreClientTests
{
    private MongoClient _mongoClient = null!;
    private MongoEventStoreClient<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var codecOptions = new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = new EventTypeMapBuilder().MapType<TestEvent>().Build(),
            EventSerde = new BsonDocumentObjectSerde(),
            MetadataSerde = new BsonDocumentMetadataSerde()
        };

        var options = new MongoEventStoreClientOptions<IDomainEvent>
        {
            CollectionOptions = EventStoreCollectionOptions.Default,
            EventCodec = EventCodec.Create(codecOptions)
        };

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _client = new MongoEventStoreClient<IDomainEvent>(_mongoClient, options);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_mongoClient, options.CollectionOptions);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
    }
}