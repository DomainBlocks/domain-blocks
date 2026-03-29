using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema;
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
public class MongoEventStoreClientTests : EventStoreClientTests
{
    private MongoClient _mongoClient = null!;
    private ILoggerFactory _loggerFactory = null!;
    private CancellationTokenSource _stopCts = null!;
    private MongoEventStoreClientOptions _options = null!;
    private IChangeStreamConnection _changeStreamConnection = null!;
    private Task _leaseContenderTask = null!;
    private MongoEventStoreClient<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
            .SetMinimumLevel(LogLevel.Debug));

        _stopCts = new CancellationTokenSource();

        _options = new MongoEventStoreClientOptions
        {
            DatabaseName = "domainblocks_tests"
        };

        var db = _mongoClient.GetDatabase(_options.DatabaseName);

        await MongoEventStoreAdmin.EnsureInitializedAsync(_mongoClient, _options);

        var filterBuilder = Builders<ChangeStreamDocument<BsonDocument>>.Filter;

        var filter = (filterBuilder.Eq("ns.coll", _options.AppendRequestsCollectionName) &
                      filterBuilder.Eq("operationType", "insert")) |
                     (filterBuilder.Eq("ns.coll", _options.EventLogCollectionName) &
                      filterBuilder.Eq("fullDocument.eventName", nameof(AppendBatchRecorded))) |
                     filterBuilder.Eq("ns.coll", _options.LeasesCollectionName);

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(filter);

        var changeStreamSubject = await db.CreateSubjectAsync(
            pipeline,
            logger: _loggerFactory.CreateLogger("ChangeStream"));

        // Set up CommitTracker
        var commitTracker = new CommitTracker(_options, _loggerFactory.CreateLogger<CommitTracker>());
        changeStreamSubject.Attach(commitTracker);

        // Set up LeaseContender
        var leaseStore = new LeaseStore(db.GetCollection<LeaseDocument>(_options.LeasesCollectionName));
        var leaseClient = new LeaseClient(leaseStore, _loggerFactory.CreateLogger<LeaseClient>());
        var leaseContender = new LeaseContender(leaseClient, _loggerFactory.CreateLogger<LeaseContender>());

        var requests = db.GetCollection<BsonDocument>(_options.AppendRequestsCollectionName);

        var leaseObserver = new LeaderLeaseObserver(
            requests,
            db.GetCollection<BsonDocument>(_options.EventLogCollectionName),
            changeStreamSubject,
            _loggerFactory);

        // Connect change stream
        _changeStreamConnection = changeStreamSubject.Connect();

        // Run LeaseContender
        _leaseContenderTask = leaseContender.RunAsync(leaseObserver, _stopCts.Token);

        _client = new MongoEventStoreClient<IDomainEvent>(
            requests,
            commitTracker,
            _options,
            GetEventCodec(),
            _loggerFactory.CreateLogger<MongoEventStoreClient<IDomainEvent>>());

        await leaseObserver.Liveliness;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _changeStreamConnection.DisposeAsync();
        await _stopCts.CancelAsync();
        await _leaseContenderTask;

        await _mongoClient.DropDatabaseAsync(_options.DatabaseName);

        await _client.DisposeAsync();
        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendOneTest(CancellationToken ct)
    {
        await DoAppend("append-one", ct);
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

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_MeasureThroughputCeiling(CancellationToken ct)
    {
        // Finds the maximum sustainable ops/sec using a single sliding window of
        // maxInFlight concurrent operations. Time-bounded so the measurement is taken
        // at steady state, not as a drain of a fixed batch.
        const int maxInFlight = 2000;
        const int warmUpSeconds = 3;
        const int measureSeconds = 15;

        var semaphore = new SemaphoreSlim(maxInFlight, maxInFlight);
        var ops = 0;
        var errors = 0;
        var isInMeasureWindow = new StrongBox<bool>(false);

        var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(warmUpSeconds + measureSeconds));

        var pendingTasks = new ConcurrentBag<Task>();

        var producerLoopTask = Task.Run(
            async () =>
            {
                using (runCts)
                {
                    while (!runCts.IsCancellationRequested)
                    {
                        try
                        {
                            await semaphore.WaitAsync(runCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        var isMeasuring = isInMeasureWindow.Value;

                        var task = DoAppend($"test-{Guid.NewGuid():N}", runCts.Token).ContinueWith(
                            t =>
                            {
                                semaphore.Release();

                                if (t.IsCompletedSuccessfully)
                                {
                                    if (isMeasuring)
                                        Interlocked.Increment(ref ops);
                                }
                                else if (t.IsFaulted)
                                {
                                    Interlocked.Increment(ref errors);
                                }
                            },
                            TaskScheduler.Default);

                        pendingTasks.Add(task);
                    }
                }
            },
            ct);

        // Warm-up: let the system reach steady state before measuring.
        await Task.Delay(TimeSpan.FromSeconds(warmUpSeconds), ct);

        isInMeasureWindow.Value = true;
        var sw = Stopwatch.StartNew();

        await Task.Delay(TimeSpan.FromSeconds(measureSeconds), ct);

        isInMeasureWindow.Value = false;
        sw.Stop();

        await producerLoopTask;
        await Task.WhenAll(pendingTasks);

        var throughput = ops / sw.Elapsed.TotalSeconds;

        await TestContext.Out.WriteLineAsync($"max in-flight: {maxInFlight}");
        await TestContext.Out.WriteLineAsync($"ops measured:  {ops}");
        await TestContext.Out.WriteLineAsync($"errors:        {errors}");
        await TestContext.Out.WriteLineAsync($"elapsed:       {sw.Elapsed.TotalMilliseconds:F0} ms");
        await TestContext.Out.WriteLineAsync($"throughput:    {throughput:F0} ops/sec");
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

    private static EventCodec<IDomainEvent, BsonValue, BsonValue> GetEventCodec()
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

        return new EventCodec<IDomainEvent, BsonValue, BsonValue>
        {
            Encoder = EventEncoder.Create(encoderOptions),
            Decoder = EventDecoder.Create(decoderOptions)
        };
    }
}