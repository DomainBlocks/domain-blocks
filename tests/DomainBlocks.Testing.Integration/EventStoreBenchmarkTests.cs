using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreBenchmarkTests<TStreamPos, TLogPos> :
    EventStoreTestBase<object, string, TStreamPos, TLogPos>
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly EventTypeMap _eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        EventStore = CreateEventStore(_eventTypeMap);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (EventStore is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
    }

    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SingleAppend_MeasureLatency(CancellationToken ct)
    {
        const int warmupIterations = 10;
        const int iterations = 100;

        for (var i = 0; i < warmupIterations; i++)
            await AppendAsync(EventStore, "warmup", ct);

        var latencies = new List<double>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var streamId = $"test-{Guid.NewGuid():N}";
            var sw = Stopwatch.StartNew();
            await AppendAsync(EventStore, streamId, ct);
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
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_MeasureThroughputCeiling(CancellationToken ct)
    {
        const int instanceCount = 1;
        const int maxInFlight = 1000;
        const int warmUpSeconds = 3;
        const int measureSeconds = 15;

        // Create a pool of event store instances
        var instances = new IEventStore<object, string, TStreamPos, TLogPos>[instanceCount];
        for (var i = 0; i < instanceCount; i++)
            instances[i] = CreateEventStore(_eventTypeMap, loggerNameSuffix: $"_{i}");

        try
        {
            var semaphore = new SemaphoreSlim(maxInFlight, maxInFlight);
            var ops = 0;
            var errors = 0;
            var isInMeasureWindow = new StrongBox<bool>(false);
            var random = Random.Shared;

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
                            var instance = instances[random.Next(instanceCount)]; // randomly pick an instance

                            var task = AppendAsync(instance, $"test-{Guid.NewGuid():N}", runCts.Token).ContinueWith(
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
            await Task.WhenAll(pendingTasks).WaitAsync(ct);

            var throughput = ops / elapsed.TotalSeconds;

            await TestContext.Out.WriteLineAsync($"instances:     {instanceCount}");
            await TestContext.Out.WriteLineAsync($"max in-flight: {maxInFlight:N0}");
            await TestContext.Out.WriteLineAsync($"ops measured:  {ops:N0}");
            await TestContext.Out.WriteLineAsync($"errors:        {errors:N0}");
            await TestContext.Out.WriteLineAsync($"elapsed:       {elapsed.TotalMilliseconds:N0} ms");
            await TestContext.Out.WriteLineAsync($"throughput:    {throughput:N0} ops/sec");
        }
        finally
        {
            foreach (var instance in instances.OfType<IAsyncDisposable>())
                await instance.DisposeAsync().AsTask().WaitAsync(ct);
        }
    }

    private static async Task AppendAsync(
        IEventStore<object, string, TStreamPos, TLogPos> instance,
        string streamId,
        CancellationToken ct)
    {
        object[] events = [new TestEvent { Value = "Benchmark" }];

        await instance.AppendAsync(streamId, events, cancellationToken: ct);
    }
}