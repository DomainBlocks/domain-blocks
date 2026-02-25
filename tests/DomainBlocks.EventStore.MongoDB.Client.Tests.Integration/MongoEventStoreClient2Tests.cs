using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Appender;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
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

        var options = new MongoEventStoreClientOptions2<IDomainEvent>
        {
            NamespaceSettings = EventStoreNamespaceSettings.Default,
            EventCodec = new EventCodec<IDomainEvent, BsonValue, BsonValue>
            {
                Encoder = EventEncoder.Create(encoderOptions),
                Decoder = EventDecoder.Create(decoderOptions)
            }
        };

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _client = new MongoEventStoreClient2<IDomainEvent>(_mongoClient, options);

        await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, options.NamespaceSettings);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ScratchTest(CancellationToken ct)
    {
        var db = _mongoClient.GetDatabase(EventStoreNamespaceSettings.Default.DatabaseName);
        var leases = db.GetCollection<LeaseDocument>(EventStoreNamespaceSettings.Default.LeasesCollectionName);

        using var loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));

        var leaseStore = new LeaseStore(leases);
        var leaseClientLogger = loggerFactory.CreateLogger<LeaseClient>();
        var leaseClient = new LeaseClient(leaseStore, leaseClientLogger);

        var commitCoordinatorLogger = loggerFactory.CreateLogger<AppenderNode>();

        var appenderNode = new AppenderNode(
            _mongoClient,
            EventStoreNamespaceSettings.Default,
            leaseClient,
            commitCoordinatorLogger);

        var appenderNodeTask = appenderNode.RunAsync(ct);

        var streamId = $"test-{Guid.NewGuid():N}";

        AppendEvent<IDomainEvent>[] events =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        var options = new AppendToStreamOptions
        {
            ExpectedState = ExpectedStreamState.Any,
            CommitId = Guid.CreateVersion7()
        };

        await Client.AppendToStreamAsync(streamId, events, options, ct);
        await Client.AppendToStreamAsync(streamId, events, options, ct);

        await appenderNodeTask.WaitAsync(ct);
    }
}