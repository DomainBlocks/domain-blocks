using System.Diagnostics;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Append and read benchmarks shared by every store. Each append test writes one small event per append to a new
/// stream, so it measures the store's ceiling rather than a realistic workload. Results are printed to the test output.
/// An append test fails only if an append fails, and a read test fails only if it reads the wrong number of events.
/// </summary>
[Category("Benchmark")]
public abstract class EventStoreBenchmarkTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly EventTypeMap _eventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    // A benchmark's result must not depend on which tests ran before it.
    protected override bool ResetLogBeforeEachTest => true;

    /// <summary>
    /// Unloaded append latency: one append in flight at a time.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_MeasureLatency(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore(_eventTypeMap);

        var runner = new AppendBenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            (_, streamId, token) => AppendAsync(eventStore, streamId, token),
            new LatencyOptions(),
            ct);

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await Harness.DescribeAsync());
        await BenchmarkReport.WriteLatencyAsync(
            "append latency, 1 in flight, 1 event per append, new stream per append", result);

        result.Errors.ShouldBe(0, "a latency figure with failed appends is not meaningful");
        result.Latencies.TotalCount.ShouldBeGreaterThan(0);
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
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
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

            await BenchmarkReport.WriteEnvironmentAsync(instances[0].GetType(), await Harness.DescribeAsync());

            await BenchmarkReport.WriteThroughputAsync(
                $"append throughput, {instanceCount} instance(s), {inFlight:N0} in flight, " +
                "1 event per append, new stream per append",
                result);

            result.Errors.ShouldBe(0, "a throughput figure with failed appends is not meaningful");
            result.Completed.ShouldBeGreaterThan(0);
        }
        finally
        {
            foreach (var instance in instances)
                await instance.DisposeAsync();
        }
    }

    /// <summary>
    /// Read throughput: one reader reads a log of a hundred streams from start to end, several times over. The median
    /// pass is reported, along with what the passes allocated.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task ReadAll_MeasureThroughput(CancellationToken ct)
    {
        var result = await MeasureReadAllAsync("read all, forward, with metadata", ReadAllOptions.Default, ct);

        result.EventsRead.ShouldBe(result.EventsInLog);
    }

    /// <summary>
    /// The same read with a filter. A stream is a hundredth of the log, and stream IDs are indexed. A tenant is a tenth
    /// of the log, and metadata is not indexed.
    /// </summary>
    [TestCase("1% by stream", 1_000)]
    [TestCase("10% by tenant", 10_000)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task ReadAll_WithFilter_MeasureThroughput(string selection, int expectedCount, CancellationToken ct)
    {
        var filter = selection == "1% by stream"
            ? EventFilter.StreamIds("stream-042")
            : EventFilter.Metadata("tenant", "tenant-4");

        var options = new ReadAllOptions { Filter = filter };
        var result = await MeasureReadAllAsync($"read all, forward, with metadata, {selection}", options, ct);

        result.EventsRead.ShouldBe(expectedCount);
    }

    /// <summary>
    /// Appends a log in which each stream is a hundredth of the events and each tenant a tenth, then reads it with the
    /// given options.
    /// </summary>
    private async Task<ReadThroughputResult> MeasureReadAllAsync(
        string title,
        ReadAllOptions options,
        CancellationToken ct)
    {
        const int streamCount = 100;
        const int eventsPerStream = 1_000;
        const int eventsInLog = streamCount * eventsPerStream;

        // The first passes warm up the store and the database, so they are not measured.
        const int warmUpPasses = 5;
        const int measuredPasses = 9;

        await using var eventStore = CreateEventStore(_eventTypeMap);

        for (var stream = 0; stream < streamCount; stream++)
        {
            var events = Enumerable
                .Range(0, eventsPerStream)
                .Select(i => AppendableEvent.Create<object>(
                    new TestEvent { Value = $"value-{i}" },
                    [KeyValuePair.Create("tenant", $"tenant-{i % 10}")]));

            await eventStore.AppendAsync($"stream-{stream:D3}", events, cancellationToken: ct);
        }

        var eventsRead = 0;

        try
        {
            for (var pass = 0; pass < warmUpPasses; pass++)
                eventsRead = await CountAsync(eventStore.ReadAll(options: options), ct);
        }
        catch (Exception ex) when (ex is NotSupportedException or EventFilterNotSupportedException)
        {
            Assert.Ignore($"The store does not support this read: {ex.Message}");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var passes = new TimeSpan[measuredPasses];
        var gcBefore = GcSnapshot.Capture();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        for (var pass = 0; pass < measuredPasses; pass++)
        {
            var start = Stopwatch.GetTimestamp();
            eventsRead = await CountAsync(eventStore.ReadAll(options: options), ct);
            passes[pass] = Stopwatch.GetElapsedTime(start);
        }

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var gc = GcSnapshot.Capture().Since(gcBefore);
        var result = new ReadThroughputResult(eventsInLog, eventsRead, passes, allocated, gc);

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await Harness.DescribeAsync());
        await BenchmarkReport.WriteReadThroughputAsync(title, result);

        return result;
    }

    private static async Task<int> CountAsync(
        IAsyncEnumerable<ReadEvent<object, string, TStreamPos, TLogPos>> events,
        CancellationToken ct)
    {
        var count = 0;

        await foreach (var _ in events.WithCancellation(ct).ConfigureAwait(false))
            count++;

        return count;
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