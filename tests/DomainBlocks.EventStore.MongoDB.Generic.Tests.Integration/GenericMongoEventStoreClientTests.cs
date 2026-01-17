using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Generic.Tests.Integration;

[TestFixture]
public class GenericMongoEventStoreClientTests : EventStoreClientTests
{
    private MongoClient _mongoClient = null!;
    private MongoEventStoreClient<IDomainEvent, EventDocument> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var codecOptions = new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = new EventTypeMapBuilder().MapType<TestEvent>().Build(),
            EventSerializer = new BsonDocumentSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        };

        var options = new MongoEventStoreClientOptions<IDomainEvent, EventDocument>
        {
            CollectionOptions = EventStoreCollectionOptions.Default,
            EventDocumentSchema = EventDocumentSchema.Default,
            EventDocumentCodec = EventDocumentCodec.Create(EventCodec.Create(codecOptions))
        };

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _client = new MongoEventStoreClient<IDomainEvent, EventDocument>(_mongoClient, options);

        await MongoEventStoreAdmin.EnsureIndexesAsync(
            _mongoClient,
            options.CollectionOptions,
            options.EventDocumentSchema);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
    }
}