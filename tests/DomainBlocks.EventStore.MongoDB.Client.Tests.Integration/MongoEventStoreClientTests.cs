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

        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<TestEvent>()
            .Build();

        var writeCodecOptions = new EventCodecOptions<IDomainEvent, byte[], byte[]>
        {
            TypeMap = eventTypeMap,
            EventSerializer = new RawBsonObjectSerializer(),
            MetadataSerializer = new RawBsonMetadataSerializer()
        };

        var readCodecOptions = new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerializer = new BsonDocumentSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        };

        var writeCodec = EventCodec.Create(writeCodecOptions);
        var readCodec = EventCodec.Create(readCodecOptions);

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _client = new MongoEventStoreClient<IDomainEvent>(_mongoClient, appenderClient, writeCodec, readCodec);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_mongoClient, EventStoreCollectionOptions.Default);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _grpcChannel.Dispose();
        _mongoClient.Dispose();
    }
}