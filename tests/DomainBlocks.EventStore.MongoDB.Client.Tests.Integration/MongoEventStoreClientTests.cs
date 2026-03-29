using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
public class MongoEventStoreClientTests : EventStoreClientTests
{
    private MongoClient _mongoClient = null!;
    private ILoggerFactory _loggerFactory = null!;
    private CancellationTokenSource _stopCts = null!;
    private MongoEventStoreClientOptions2<IDomainEvent> _options = null!;
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

        _options = GetEventStoreClientOptions();
        var ns = _options.NamespaceSettings;
        var db = _mongoClient.GetDatabase(ns.DatabaseName);

        await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, ns);

        var filterBuilder = Builders<ChangeStreamDocument<BsonDocument>>.Filter;

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(filterBuilder.Or(
                // AppendRequest inserts only - excludes completer's UpdateManyAsync events
                filterBuilder.And(
                    filterBuilder.Eq("ns.coll", ns.AppendRequestsCollectionName),
                    filterBuilder.Eq("operationType", "insert")),

                // AppendBatchCompleted entries only — excludes the ~100 regular event writes
                filterBuilder.And(
                    filterBuilder.Eq("ns.coll", ns.EventLogCollectionName),
                    filterBuilder.Eq("fullDocument.eventName", "AppendBatchCompleted")),

                // All lease events — commit position updates
                filterBuilder.Eq("ns.coll", ns.LeasesCollectionName)
            ));

        var changeStreamSubject = await db.CreateSubjectAsync(
            pipeline,
            new ChangeStreamSubjectOptions
            {
                MongoOptions = new ChangeStreamOptions
                {
                    BatchSize = 1000
                }
            },
            _loggerFactory.CreateLogger("ChangeStream"));

        // Set up AppendRequestTracker
        var requestTracker = new AppendRequestTracker(ns, _loggerFactory.CreateLogger<AppendRequestTracker>());
        changeStreamSubject.Attach(requestTracker);

        // Set up LeaseContender
        var leaseStore = new LeaseStore(db.GetCollection<LeaseDocument>(ns.LeasesCollectionName));
        var leaseClient = new LeaseClient(leaseStore, _loggerFactory.CreateLogger<LeaseClient>());
        var leaseContender = new LeaseContender(leaseClient, _loggerFactory.CreateLogger<LeaseContender>());

        var requests = db.GetCollection<BsonDocument>(ns.AppendRequestsCollectionName);

        var leaseObserver = new EventAppenderLeaseObserver(
            requests,
            db.GetCollection<BsonDocument>(ns.EventLogCollectionName),
            changeStreamSubject,
            _loggerFactory);

        // Connect change stream
        _changeStreamConnection = changeStreamSubject.Connect();

        // Run LeaseContender
        _leaseContenderTask = leaseContender.RunAsync([leaseObserver], _stopCts.Token);

        _client = new MongoEventStoreClient<IDomainEvent>(
            requests,
            requestTracker,
            _options,
            _loggerFactory.CreateLogger<MongoEventStoreClient<IDomainEvent>>());

        await leaseObserver.Liveliness;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _changeStreamConnection.DisposeAsync();
        await _stopCts.CancelAsync();
        await _leaseContenderTask;

        await _mongoClient.DropDatabaseAsync(_options.NamespaceSettings.DatabaseName);

        await _client.DisposeAsync();
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

    // [Test]
    // [CancelAfter(TestTimeoutMillis)]
    // public async Task AppendToStreamAsync_ConcurrentAppends_MeasureThroughput(CancellationToken ct)
    // {
    //     const int concurrency = 20;
    //     const int opsPerProducer = 1000;
    //     const int totalOps = concurrency * opsPerProducer;
    //     const int maxInFlight = 500;
    //
    //     var semaphore = new SemaphoreSlim(maxInFlight);
    //
    //     // Warm up
    //     await Task.WhenAll(Enumerable
    //         .Range(0, concurrency)
    //         .Select(_ => Task.Run(() => DoAppend($"warmup-{Guid.NewGuid():N}", ct), ct)));
    //
    //     var sw = Stopwatch.StartNew();
    //
    //     await Task.WhenAll(
    //         Enumerable
    //             .Range(0, concurrency)
    //             .Select(_ => Task.Run(async () =>
    //                 {
    //                     var pending = new List<Task>(opsPerProducer);
    //
    //                     for (var i = 0; i < opsPerProducer; i++)
    //                     {
    //                         await semaphore.WaitAsync(ct);
    //
    //                         pending.Add(Append());
    //
    //                         continue;
    //
    //                         async Task Append()
    //                         {
    //                             try
    //                             {
    //                                 await DoAppend($"test-{Guid.NewGuid():N}", ct);
    //                             }
    //                             finally
    //                             {
    //                                 semaphore.Release();
    //                             }
    //                         }
    //                     }
    //
    //                     await Task.WhenAll(pending);
    //                 },
    //                 ct)));
    //
    //     sw.Stop();
    //     var opsPerSecond = totalOps / sw.Elapsed.TotalSeconds;
    //
    //     await TestContext.Out.WriteLineAsync($"concurrency: {concurrency}");
    //     await TestContext.Out.WriteLineAsync($"maxInFlight: {maxInFlight}");
    //     await TestContext.Out.WriteLineAsync($"total ops:   {totalOps}");
    //     await TestContext.Out.WriteLineAsync($"elapsed:     {sw.Elapsed.TotalMilliseconds:F0} ms");
    //     await TestContext.Out.WriteLineAsync($"throughput:  {opsPerSecond:F1} ops/sec");
    // }

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

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(warmUpSeconds + measureSeconds));

        var semaphore = new SemaphoreSlim(maxInFlight, maxInFlight);
        var ops = 0L;
        var errors = 0L;
        var isInMeasureWindow = new StrongBox<bool>(false);

        var pendingTasks = new ConcurrentBag<Task>();

        // Single loop: acquire slot → fire append (non-blocking) → repeat.
        // Keeps exactly maxInFlight operations in flight at all times while running.
        var loopTask = Task.Run(
            async () =>
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

                    // Capture before firing — ContinueWith may run after the window closes.
                    var isMeasuring = isInMeasureWindow.Value;

                    var task = DoAppend($"test-{Guid.NewGuid():N}", runCts.Token)
                        .ContinueWith(
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
            },
            ct);

        // Warm-up: let the system reach steady state before measuring.
        await Task.Delay(TimeSpan.FromSeconds(warmUpSeconds), ct);

        isInMeasureWindow.Value = true;
        var sw = Stopwatch.StartNew();

        await Task.Delay(TimeSpan.FromSeconds(measureSeconds), ct);

        isInMeasureWindow.Value = false;
        sw.Stop();

        // Wait for the producer loop to notice cancellation, then drain all
        // in-flight ops so the test exits cleanly.
        await loopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // for (var i = 0; i < maxInFlight; i++)
        //     await semaphore.WaitAsync(ct);

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