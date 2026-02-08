using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Client.Tests.Integration;

[TestFixture]
public class MongoEventStoreClient2Tests : EventStoreClientTests
{
    private MongoClient _mongoClient = null!;
    private MongoEventStoreClient2<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        var encoderOptions = new EventEncoderOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap.Appends,
            EventSerializer = new BsonDocumentObjectSerde(),
            MetadataSerializer = new BsonDocumentMetadataSerde()
        };

        var decoderOptions = new EventDecoderOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap.Reads,
            EventDeserializer = new BsonDocumentObjectSerde(),
            MetadataDeserializer = new BsonDocumentMetadataSerde()
        };

        var options = new MongoEventStoreClientOptions<IDomainEvent>
        {
            CollectionOptions = EventStoreCollectionOptions.Default,
            EventCodec = new EventCodec<IDomainEvent, BsonValue, BsonValue>
            {
                Encoder = EventEncoder.Create(encoderOptions),
                Decoder = EventDecoder.Create(decoderOptions)
            }
        };

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _client = new MongoEventStoreClient2<IDomainEvent>(_mongoClient, options);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_mongoClient, EventStoreCollectionOptions.Default);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
    }
}