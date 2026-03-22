using System.Diagnostics;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
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
    private MongoClient _mongoClient = null!;
    private ILoggerFactory _loggerFactory = null!;
    private CancellationTokenSource _stopCts = null!;
    private IChangeStreamConnection _changeStreamConnection = null!;
    private Task _leaseContenderTask = null!;
    private MongoEventStoreClient2<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _stopCts = new CancellationTokenSource();

        var options = GetEventStoreClientOptions();
        var ns = options.NamespaceSettings;
        var db = _mongoClient.GetDatabase(ns.DatabaseName);

        await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, ns);

        var changeStreamSubject = await db.CreateSubjectAsync();

        // Set up AppendRequestTracker
        var requestTracker = new AppendRequestTracker(ns);
        changeStreamSubject.Attach(requestTracker);

        // Set up LeaseContender
        var leaseStore = new LeaseStore(db.GetCollection<LeaseDocument>(ns.LeasesCollectionName));
        var leaseClient = new LeaseClient(leaseStore, _loggerFactory.CreateLogger<LeaseClient>());
        var leaseContender = new LeaseContender(leaseClient, _loggerFactory.CreateLogger<LeaseContender>());

        var leaseObserver = new EventAppenderLeaseObserver(
            db.GetCollection<AppendRequest>(ns.AppendRequestsCollectionName),
            db.GetCollection<EventLogEntry>(ns.EventLogCollectionName),
            changeStreamSubject,
            _loggerFactory);

        // Connect change stream
        _changeStreamConnection = changeStreamSubject.Connect();

        // Run LeaseContender
        _leaseContenderTask = leaseContender.RunAsync([leaseObserver], _stopCts.Token);

        _client = new MongoEventStoreClient2<IDomainEvent>(_mongoClient, requestTracker, options);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _changeStreamConnection.DisposeAsync();
        await _stopCts.CancelAsync();
        await _leaseContenderTask;
        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_SingleAppend_MeasureLatency(CancellationToken ct)
    {
        // Warm up: first request pays connection/change-stream setup costs.
        await DoAppend("warmup", ct);

        const int iterations = 200;
        var latencies = new List<double>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var streamId = $"test-{Guid.NewGuid():N}";
            var sw = Stopwatch.StartNew();
            await DoAppend(streamId, ct);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var sorted = latencies.OrderBy(x => x).ToList();
        await TestContext.Out.WriteLineAsync($"p50:  {sorted[Percentile(0.50)]:F1} ms");
        await TestContext.Out.WriteLineAsync($"p90:  {sorted[Percentile(0.90)]:F1} ms");
        await TestContext.Out.WriteLineAsync($"p99:  {sorted[Percentile(0.99)]:F1} ms");
        await TestContext.Out.WriteLineAsync($"min:  {sorted[0]:F1} ms");
        await TestContext.Out.WriteLineAsync($"max:  {sorted[^1]:F1} ms");
        await TestContext.Out.WriteLineAsync($"mean: {latencies.Average():F1} ms");

        return;

        int Percentile(double p) => (int)Math.Ceiling(sorted.Count * p) - 1;
    }

    private async Task DoAppend(string streamId, CancellationToken ct)
    {
        AppendEvent<IDomainEvent>[] events = [CreateTestEvent("Evt1")];

        var options = new AppendToStreamOptions
        {
            ExpectedState = ExpectedStreamState.Any,
            CommitId = Guid.CreateVersion7()
        };

        await Client.AppendToStreamAsync(streamId, events, options, ct);
    }

    private static MongoEventStoreClientOptions2<IDomainEvent> GetEventStoreClientOptions()
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