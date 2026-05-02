using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreClient2ConcurrencyTests2
{
    private const int WriterCount = 5;

    private MongoEventStoreClientOptions2 _options = null!;
    private TestMongoEventStoreClient2Factory<object> _clientFactory = null!;
    private IMongoClient _mongoClient = null!;
    private ITestEventStoreClientHandle<object>[] _clientHandles = null!;

    [SetUp]
    public async Task SetUp()
    {
        _options = new MongoEventStoreClientOptions2 { DatabaseName = "domainblocks_concurrency_tests_v2" };
        _clientFactory = TestMongoEventStoreClient2Factory.CreateDefault(_options);
        _mongoClient = _clientFactory.MongoClient;

        _clientHandles = new ITestEventStoreClientHandle<object>[WriterCount];

        for (var i = 0; i < WriterCount; i++)
            _clientHandles[i] = await _clientFactory.CreateAsync($"client_{i}");
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var clientHandle in _clientHandles)
            await clientHandle.DisposeAsync();

        await _clientFactory.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task LongRunning_ChangeStream_GlobalPositions_AreStrictlyIncreasingAndContiguous_FailFast(
        CancellationToken ct)
    {
        const int runSeconds = 15;
        const int minObservedEvents = 200;

        var db = _mongoClient.GetDatabase(_options.DatabaseName);
        var eventLog = db.GetCollection<BsonDocument>(_options.EventLogCollectionName);

        var runId = Guid.NewGuid().ToString("N");

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(runSeconds));
        var runToken = runCts.Token;

        // Capture first assertion failure from observer and fail after coordinated shutdown.
        Exception? firstFailure = null;

        // Watch inserts on event log. We filter to this test run inside the loop.
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(x => x.OperationType == ChangeStreamOperationType.Insert);

        var changeStreamOptions = new ChangeStreamOptions
        {
            FullDocument = ChangeStreamFullDocumentOption.Default
        };

        // Open change stream before writers start so we do not miss early inserts.
        using var cursor = await eventLog.WatchAsync(pipeline, changeStreamOptions, runToken);

        var observerTask = Task.Run(
            async () =>
            {
                long? lastPos = null;
                var observed = 0;

                try
                {
                    await foreach (var change in cursor.ToAsyncEnumerable().WithCancellation(runToken))
                    {
                        var doc = change.FullDocument;
                        if (doc is null)
                            continue;

                        var pos = doc["_id"].AsInt64;

                        if (lastPos.HasValue && pos != lastPos.Value + 1)
                        {
                            firstFailure ??= new ShouldAssertException(
                                $"Global position not contiguous. Last={lastPos.Value}, Current={pos}, RunId={runId}");
                            await runCts.CancelAsync(); // Fail fast: stop all writers and observer quickly.
                            return;
                        }

                        lastPos = pos;
                        observed++;
                    }
                }
                catch (OperationCanceledException) when (runToken.IsCancellationRequested)
                {
                    // Expected when run duration elapses or fail-fast cancellation occurs.
                }

                if (firstFailure is null)
                {
                    observed.ShouldBeGreaterThanOrEqualTo(
                        minObservedEvents,
                        $"Expected to observe at least {minObservedEvents} events for RunId={runId}");
                }
            },
            runToken);

        var appendTasks = _clientHandles
            .Select((handle, i) => Task.Run(async () =>
                {
                    var streamId = $"lr-{runId}-w{i}";
                    var sequence = 0;

                    while (!runToken.IsCancellationRequested)
                    {
                        await handle.Client.AppendToStreamAsync(
                            streamId,
                            [
                                new TestEvent { Value = $"{runId}|w{i}|e{sequence++}" }
                            ],
                            new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
                            runToken);
                    }
                },
                runToken))
            .ToList();

        // Wait for run end or fail-fast cancellation.
        try
        {
            await Task.WhenAll(appendTasks.Append(observerTask));
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            // Normal shutdown path.
        }

        // Surface the first observer failure as the test failure.
        if (firstFailure is not null)
            throw firstFailure;
    }
}