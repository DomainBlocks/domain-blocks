using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientBenchmarkTests : EventStoreClientTestBase<object>
{
    private ITestEventStoreClientFactory<object> _clientFactory = null!;
    private ITestEventStoreClientHandle<object> _clientHandle = null!;
    private IEventStoreClient<object> _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _clientFactory = await GetClientFactoryAsync();
        _clientHandle = await _clientFactory.CreateAsync();
        _client = _clientHandle.Client;
    }

    [TearDown]
    public async Task TearDown()
    {
        await _clientHandle.DisposeAsync();
        await _clientFactory.DisposeAsync();
    }

    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_SingleAppend_MeasureLatency(CancellationToken ct)
    {
        const int warmupIterations = 10;
        const int iterations = 100;

        for (var i = 0; i < warmupIterations; i++)
            await AppendAsync("warmup", ct);

        var latencies = new List<double>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var streamId = $"test-{Guid.NewGuid():N}";
            var sw = Stopwatch.StartNew();
            await AppendAsync(streamId, ct);
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
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_MeasureThroughputCeiling(CancellationToken ct)
    {
        const int maxInFlight = 1000;
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

                        var task = AppendAsync($"test-{Guid.NewGuid():N}", runCts.Token).ContinueWith(
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

        // Warm-up
        await Task.Delay(TimeSpan.FromSeconds(warmUpSeconds), ct);

        // Measure
        isInMeasureWindow.Value = true;
        var start = Stopwatch.GetTimestamp();
        await Task.Delay(TimeSpan.FromSeconds(measureSeconds), ct);

        // Stop
        isInMeasureWindow.Value = false;
        var elapsed = Stopwatch.GetElapsedTime(start);
        await producerLoopTask;
        await Task.WhenAll(pendingTasks);

        var throughput = ops / elapsed.TotalSeconds;

        await TestContext.Out.WriteLineAsync($"max in-flight: {maxInFlight}");
        await TestContext.Out.WriteLineAsync($"ops measured:  {ops}");
        await TestContext.Out.WriteLineAsync($"errors:        {errors}");
        await TestContext.Out.WriteLineAsync($"elapsed:       {elapsed.TotalMilliseconds:F0} ms");
        await TestContext.Out.WriteLineAsync($"throughput:    {throughput:F0} ops/sec");
    }

    private async Task AppendAsync(string streamId, CancellationToken ct)
    {
        object[] events = [new TestEvent { Value = "Benchmark" }];

        var options = new AppendToStreamOptions
        {
            ExpectedState = ExpectedStreamState.Any,
            CommitId = Guid.CreateVersion7()
        };

        await _client.AppendToStreamAsync(streamId, events, options, ct);
    }
}