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
/// Benchmarks shared by every store. Each append test writes one small event per append to a new stream, which is the
/// cheapest possible append and therefore measures the store's ceiling rather than a workload. Results are printed
/// to the test output; the tests only fail if an operation errors, since a throughput figure with errors is invalid.
/// </summary>
[Category("Benchmark")]
public abstract class EventStoreBenchmarkTests<TStreamPos, TLogPos>(IEventStoreTestHarness<TStreamPos, TLogPos> harness)
    :
        EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    // Held once, as predicates are equal only if they are the same one.
    private static readonly System.Linq.Expressions.Expression<Func<TestEvent, bool>> HasTheValue =
        e => e.Value == "value-7";

    private readonly EventTypeMap _eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

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
    /// Read throughput: how long one reader takes to read a log of a hundred streams from start to end.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public Task ReadAll_MeasureThroughput(CancellationToken ct) =>
        MeasureReadAllAsync("read all, forward, with metadata", ReadAllOptions.Default, ct);

    /// <summary>
    /// The same read with a filter, evaluated by the database as far as it can be, or as the events are read. A stream
    /// is a hundredth of the log, and its id is indexed. A tenant is a tenth of it, and metadata is not.
    /// </summary>
    [TestCase("1% by stream", FilterPushdownMode.Prefer)]
    [TestCase("1% by stream", FilterPushdownMode.None)]
    [TestCase("10% by tenant", FilterPushdownMode.Prefer)]
    [TestCase("10% by tenant", FilterPushdownMode.None)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public Task ReadAll_WithAFilter_MeasureThroughput(string selection, FilterPushdownMode pushdownMode, CancellationToken ct)
    {
        var filter = selection == "1% by stream"
            ? EventFilter.StreamId("stream-042")
            : EventFilter.Metadata("tenant", "tenant-4");

        return MeasureReadAllAsync(
            $"read all, forward, {selection}, pushdown {pushdownMode}",
            new ReadAllOptions { Filter = filter, FilterPushdownMode = pushdownMode },
            ct);
    }

    /// <summary>
    /// What it saves to have the database rule out the events whose stored values contradict a predicate. One event in
    /// a thousand has the value. With nothing pushed down, every event is read and the predicate tested in process.
    /// Otherwise the database narrows the read to the names of the type, which here is every event, and to the events
    /// that the stored value does not rule out.
    /// </summary>
    [TestCase(FilterPushdownMode.None)]
    [TestCase(FilterPushdownMode.Prefer)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task ReadAll_WithAPredicate_MeasureThroughput(FilterPushdownMode pushdownMode, CancellationToken ct)
    {
        // The database has to be able to see into payloads, which as bytes it cannot.
        var format = Harness.SupportedFormats.Contains(EventFormat.Bson) ? EventFormat.Bson : (EventFormat?)null;

        var result = await MeasureReadAllAsync(
            $"read all, forward, 0.1% by a predicate, pushdown {pushdownMode}",
            new ReadAllOptions { Filter = EventFilter.OfType(HasTheValue), FilterPushdownMode = pushdownMode },
            ct,
            format);

        result?.EventsRead.ShouldBe(100);
    }

    private async Task<ReadThroughputResult?> MeasureReadAllAsync(
        string title,
        ReadAllOptions options,
        CancellationToken ct,
        EventFormat? format = null)
    {
        const int streamCount = 100;
        const int eventsPerStream = 1_000;
        const int warmupPasses = 5;
        const int measuredPasses = 9;

        await using var eventStore = CreateEventStore(_eventTypeMap, format);

        // Each stream is one hundredth of the log, and each tenant one tenth of it.
        for (var stream = 0; stream < streamCount; stream++)
        {
            var events = Enumerable
                .Range(0, eventsPerStream)
                .Select(i => AppendableEvent.Create<object>(
                    new TestEvent { Value = $"value-{i}" },
                    [KeyValuePair.Create("tenant", $"tenant-{i % 10}")]));

            await eventStore.AppendAsync($"stream-{stream:D3}", events, cancellationToken: ct);
        }

        ReadThroughputResult result;

        try
        {
            result = await MeasureReadAllAsync(
                eventStore,
                options,
                streamCount * eventsPerStream,
                warmupPasses,
                measuredPasses,
                ct);
        }
        catch (NotSupportedException e)
        {
            Assert.Ignore($"The store does not do this read: {e.Message}");
            return null;
        }

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await Harness.DescribeAsync());
        await BenchmarkReport.WriteReadThroughputAsync(title, result);

        result.EventsRead.ShouldBeGreaterThan(0);

        return result;
    }

    private static async Task<ReadThroughputResult> MeasureReadAllAsync(
        IEventStore<object, string, TStreamPos, TLogPos> eventStore,
        ReadAllOptions options,
        int eventsInLog,
        int warmupPasses,
        int measuredPasses,
        CancellationToken ct)
    {
        // Passes get faster until the connection, the statements and the JIT have warmed up.
        var eventsRead = 0;

        for (var pass = 0; pass < warmupPasses; pass++)
            eventsRead = await ReadAllAsync();

        var passes = new List<TimeSpan>();
        var gcBefore = GcSnapshot.Capture();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        for (var pass = 0; pass < measuredPasses; pass++)
        {
            var startedAt = Stopwatch.GetTimestamp();
            await ReadAllAsync();
            passes.Add(Stopwatch.GetElapsedTime(startedAt));
        }

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        var gc = GcSnapshot.Capture().Since(gcBefore);

        return new ReadThroughputResult(eventsInLog, eventsRead, passes, allocated, gc);

        async Task<int> ReadAllAsync()
        {
            var count = 0;

            await foreach (var _ in eventStore.ReadAll(options: options).WithCancellation(ct))
                count++;

            return count;
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