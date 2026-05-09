using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDbClientDirectBenchmarkTests
{
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendToStreamAsync_SingleAppend_MeasureLatency(CancellationToken ct)
    {
        const int warmupIterations = 10;
        const int iterations = 100;

        await using var client = CreateClient("client_0");

        for (var i = 0; i < warmupIterations; i++)
            await AppendAsync(client, "warmup", ct);

        var latencies = new List<double>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var streamId = $"test-{Guid.NewGuid():N}";
            var sw = Stopwatch.StartNew();
            await AppendAsync(client, streamId, ct);
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
    public async Task AppendToStreamAsync_MeasureThroughputCeiling(CancellationToken ct)
    {
        const int clientCount = 1;
        const int maxInFlight = 1000;
        const int warmUpSeconds = 3;
        const int measureSeconds = 15;

        var clients = new KurrentDBClient[clientCount];
        for (var i = 0; i < clientCount; i++)
            clients[i] = CreateClient($"client_{i}");

        try
        {
            var semaphore = new SemaphoreSlim(maxInFlight, maxInFlight);
            var ops = 0;
            var errors = 0;
            var isInMeasureWindow = new StrongBox<bool>(false);
            var random = Random.Shared;

            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            runCts.CancelAfter(TimeSpan.FromSeconds(warmUpSeconds + measureSeconds));

            var pendingTasks = new ConcurrentBag<Task>();

            var producerLoopTask = Task.Run(
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

                        var isMeasuring = isInMeasureWindow.Value;
                        var client = clients[random.Next(clientCount)];

                        var task = AppendAsync(client, $"test-{Guid.NewGuid():N}", runCts.Token).ContinueWith(
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

            await TestContext.Out.WriteLineAsync($"clients:       {clientCount}");
            await TestContext.Out.WriteLineAsync($"max in-flight: {maxInFlight:N0}");
            await TestContext.Out.WriteLineAsync($"ops measured:  {ops:N0}");
            await TestContext.Out.WriteLineAsync($"errors:        {errors:N0}");
            await TestContext.Out.WriteLineAsync($"elapsed:       {elapsed.TotalMilliseconds:N0} ms");
            await TestContext.Out.WriteLineAsync($"throughput:    {throughput:N0} ops/sec");
        }
        finally
        {
            foreach (var client in clients)
            {
                if (client is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
            }
        }
    }

    private static async Task AppendAsync(KurrentDBClient client, string streamId, CancellationToken ct)
    {
        var eventData = new EventData(
            eventId: Uuid.NewUuid(),
            type: "TestEvent",
            data: """{"value":"Benchmark"}"""u8.ToArray(),
            metadata: ReadOnlyMemory<byte>.Empty);

        await client.AppendToStreamAsync(
            streamName: streamId,
            expectedState: StreamState.Any,
            eventData: [eventData],
            cancellationToken: ct);
    }

    private static KurrentDBClient CreateClient(string connectionName)
    {
        const string connectionString = "esdb://admin:changeit@localhost:2113?tls=false";

        var settings = KurrentDBClientSettings.Create(connectionString);
        settings.ConnectionName = $"benchmark-{connectionName}-{Guid.NewGuid():N}";
        settings.DefaultCredentials ??= new UserCredentials("admin", "changeit");

        return new KurrentDBClient(settings);
    }
}