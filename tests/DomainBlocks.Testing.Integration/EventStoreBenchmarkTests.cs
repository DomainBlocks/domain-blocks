using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration.Benchmarking;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

/// <summary>
/// Append benchmarks shared by every store. Each test writes one small event per append to a new stream, which is the
/// cheapest possible append and therefore measures the store's ceiling rather than a workload. Results are printed
/// to the test output; the tests only fail if an operation errors, since a throughput figure with errors is invalid.
/// </summary>
public abstract class EventStoreBenchmarkTests<TStreamPos, TLogPos> :
    EventStoreTestBase<object, string, TStreamPos, TLogPos>
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly EventTypeMap _eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    [SetUp]
    public Task SetUp() => ResetStoreAsync();

    /// <summary>
    /// Returns the store to an empty log so that a benchmark's result does not depend on which tests ran before it.
    /// </summary>
    protected virtual Task ResetStoreAsync() => Task.CompletedTask;

    /// <summary>
    /// Describes the store and any options that affect the result, for the report header.
    /// </summary>
    protected virtual Task<string?> DescribeStoreAsync() => Task.FromResult<string?>(null);

    /// <summary>
    /// Unloaded append latency: one append in flight at a time.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.BenchmarkMillis)]
    public async Task AppendAsync_MeasureLatency(CancellationToken ct)
    {
        var eventStore = CreateEventStore(_eventTypeMap);
        await using var disposable = eventStore as IAsyncDisposable;

        var runner = new AppendBenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            (_, streamId, token) => AppendAsync(eventStore, streamId, token),
            new LatencyOptions(),
            ct);

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await DescribeStoreAsync());
        await BenchmarkReport.WriteLatencyAsync("append latency, 1 in flight, 1 event per append, new stream per append", result);

        result.Errors.ShouldBe(0, "a latency figure with failed appends is not meaningful");
        result.Latencies.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Append throughput with a fixed number of closed-loop workers spread over one or more store instances. The
    /// ceiling is the plateau across the cases, not any single case; latency under that load is reported alongside.
    /// </summary>
    [TestCase(1, 1)]
    [TestCase(1, 10)]
    [TestCase(1, 100)]
    [TestCase(1, 1_000)]
    [TestCase(4, 1_000)]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.BenchmarkMillis)]
    public async Task AppendAsync_MeasureThroughput(int instanceCount, int inFlight, CancellationToken ct)
    {
        var instances = new IEventStore<object, string, TStreamPos, TLogPos>[instanceCount];
        for (var i = 0; i < instanceCount; i++)
            instances[i] = CreateEventStore(_eventTypeMap, loggerNameSuffix: $"_{i}");

        try
        {
            var runner = new AppendBenchmarkRunner();

            var result = await runner.MeasureThroughputAsync(
                (workerIndex, streamId, token) => AppendAsync(instances[workerIndex % instanceCount], streamId, token),
                new ThroughputOptions { InFlight = inFlight },
                ct);

            await BenchmarkReport.WriteEnvironmentAsync(instances[0].GetType(), await DescribeStoreAsync());
            await BenchmarkReport.WriteThroughputAsync(
                $"append throughput, {instanceCount} instance(s), {inFlight:N0} in flight, 1 event per append, new stream per append",
                result);

            result.Errors.ShouldBe(0, "a throughput figure with failed appends is not meaningful");
            result.Completed.ShouldBeGreaterThan(0);
        }
        finally
        {
            foreach (var instance in instances.OfType<IAsyncDisposable>())
                await instance.DisposeAsync();
        }
    }

    private static Task AppendAsync(
        IEventStore<object, string, TStreamPos, TLogPos> instance,
        string streamId,
        CancellationToken ct)
    {
        object[] events = [new TestEvent { Value = "Benchmark" }];
        return instance.AppendAsync(streamId, events, cancellationToken: ct);
    }
}
