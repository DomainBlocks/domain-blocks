using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Grpc.Net.Client;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Client.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientTests : EventStoreClientTests
{
    private GrpcChannel _grpcChannel = null!;
    private MongoClient _mongoClient = null!;
    private MongoEventStoreClient<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _grpcChannel = GrpcChannel.ForAddress("http://localhost:50051");
        var appenderClient = new Api.Appender.V0.AppenderService.AppenderServiceClient(_grpcChannel);

        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        var encoderOptions = new EventEncoderOptions<IDomainEvent, byte[], byte[]>
        {
            TypeMap = eventTypeMap.Appends,
            EventSerializer = new RawBsonObjectSerde(),
            MetadataSerializer = new RawBsonMetadataSerde()
        };

        var decoderOptions = new EventDecoderOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap.Reads,
            EventDeserializer = new BsonDocumentObjectSerde(),
            MetadataDeserializer = new BsonDocumentMetadataSerde()
        };

        var encoder = EventEncoder.Create(encoderOptions);
        var decoder = EventDecoder.Create(decoderOptions);

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _client = new MongoEventStoreClient<IDomainEvent>(_mongoClient, appenderClient, encoder, decoder);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_mongoClient, EventStoreCollectionOptions.Default);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _grpcChannel.Dispose();
        _mongoClient.Dispose();
    }
}