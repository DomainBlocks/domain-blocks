using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.Benchmarking;
using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

/// <summary>
/// Raw <see cref="KurrentDBClient"/> baselines, measured with the same harness and shapes as
/// <see cref="KurrentDBEventStoreBenchmarkTests"/> so the store's overhead can be read off directly.
/// </summary>
[TestFixture]
public class KurrentDbClientBenchmarkTests
{
    private const string Description = "raw KurrentDBClient, StreamState.Any, one 24-byte JSON event per append";

    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.BenchmarkMillis)]
    public async Task AppendAsync_MeasureLatency(CancellationToken ct)
    {
        await using var client = CreateClient("client_0");

        var runner = new AppendBenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            (_, streamId, token) => AppendAsync(client, streamId, token),
            new LatencyOptions(),
            ct);

        await BenchmarkReport.WriteEnvironmentAsync(typeof(KurrentDBClient), Description);
        await BenchmarkReport.WriteLatencyAsync("append latency, 1 in flight, new stream per append", result);

        result.Errors.ShouldBe(0);
        result.Latencies.Count.ShouldBeGreaterThan(0);
    }

    [TestCase(1, 1)]
    [TestCase(1, 10)]
    [TestCase(1, 100)]
    [TestCase(1, 1_000)]
    [TestCase(4, 1_000)]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.BenchmarkMillis)]
    public async Task AppendAsync_MeasureThroughput(int clientCount, int inFlight, CancellationToken ct)
    {
        var clients = new KurrentDBClient[clientCount];
        for (var i = 0; i < clientCount; i++)
            clients[i] = CreateClient($"client_{i}");

        try
        {
            var runner = new AppendBenchmarkRunner();

            var result = await runner.MeasureThroughputAsync(
                (workerIndex, streamId, token) => AppendAsync(clients[workerIndex % clientCount], streamId, token),
                new ThroughputOptions { InFlight = inFlight },
                ct);

            await BenchmarkReport.WriteEnvironmentAsync(typeof(KurrentDBClient), Description);
            await BenchmarkReport.WriteThroughputAsync(
                $"append throughput, {clientCount} client(s), {inFlight:N0} in flight, new stream per append",
                result);

            result.Errors.ShouldBe(0);
            result.Completed.ShouldBeGreaterThan(0);
        }
        finally
        {
            foreach (var client in clients)
                await client.DisposeAsync();
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
        var settings = KurrentDBClientSettings.Create(SetUpFixture.KurrentDBConnectionString);
        settings.ConnectionName = $"benchmark-{connectionName}-{Guid.NewGuid():N}";
        return new KurrentDBClient(settings);
    }
}
