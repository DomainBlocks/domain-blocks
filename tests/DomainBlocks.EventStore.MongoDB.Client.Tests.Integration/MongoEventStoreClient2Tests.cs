using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
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
    private ILoggerFactory _loggerFactory = null!;
    private CancellationTokenSource _stopCts = null!;
    private MongoClient _mongoClient = null!;
    private IChangeStreamConnection _changeStreamConnection = null!;
    private Task _leaseContenderTask = null!;
    private MongoEventStoreClient2<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _stopCts = new CancellationTokenSource();
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);

        var options = GetEventStoreClientOptions();
        var nsSettings = options.NamespaceSettings;
        var db = _mongoClient.GetDatabase(nsSettings.DatabaseName);

        var changeStreamSubject = await db.CreateSubjectAsync();

        // Set up AppendRequestTracker
        var requestTracker = new AppendRequestTracker(nsSettings.AppendRequestsCollectionNamespace);
        changeStreamSubject.Attach(requestTracker);

        // Set up LeaseContender
        var leasesCollection = db.GetCollection<LeaseDocument>(nsSettings.LeasesCollectionName);
        var leaseStore = new LeaseStore(leasesCollection);
        var leaseClientLogger = _loggerFactory.CreateLogger<LeaseClient>();
        var leaseClient = new LeaseClient(leaseStore, leaseClientLogger);
        var leaseContenderLogger = _loggerFactory.CreateLogger<LeaseContender>();
        var leaseContender = new LeaseContender(leaseClient, leaseContenderLogger);

        // Set up LeaderWorkerRunner
        var eventAppenderFactory = new EventAppenderFactory(
            nsSettings.AppendRequestsCollectionNamespace,
            _loggerFactory);

        var leaderWorkerRunner = new LeaderWorkerRunner([eventAppenderFactory], changeStreamSubject);

        // Connect change stream
        _changeStreamConnection = changeStreamSubject.Connect();

        // Run LeaseContender
        _leaseContenderTask = leaseContender.RunAsync([leaderWorkerRunner], _stopCts.Token);

        _client = new MongoEventStoreClient2<IDomainEvent>(_mongoClient, requestTracker, options);

        await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, nsSettings);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _changeStreamConnection.DisposeAsync();
        await _stopCts.CancelAsync();
        await _leaseContenderTask;
        _loggerFactory.Dispose();
        _mongoClient.Dispose();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ScratchTest(CancellationToken ct)
    {
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
    }

    private MongoEventStoreClientOptions2<IDomainEvent> GetEventStoreClientOptions()
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

        return new MongoEventStoreClientOptions2<IDomainEvent>
        {
            NamespaceSettings = EventStoreNamespaceSettings.Default,
            EventCodec = new EventCodec<IDomainEvent, BsonValue, BsonValue>
            {
                Encoder = EventEncoder.Create(encoderOptions),
                Decoder = EventDecoder.Create(decoderOptions)
            }
        };
    }
}