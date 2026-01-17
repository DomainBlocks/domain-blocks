using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Grpc.Net.Client;
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

        var codecOptions = new EventCodecOptions<IDomainEvent, byte[], byte[]>
        {
            TypeMap = new EventTypeMapBuilder().MapType<TestEvent>().Build(),
            EventSerializer = new RawBsonObjectSerializer(),
            MetadataSerializer = new RawBsonMetadataSerializer()
        };

        var codec = EventCodec.Create(codecOptions);

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _client = new MongoEventStoreClient<IDomainEvent>(_mongoClient, appenderClient, codec);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_mongoClient, EventStoreCollectionOptions.Default);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _grpcChannel.Dispose();
        _mongoClient.Dispose();
    }
}