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
/// Append and read benchmarks shared by every store. Each append test writes one small event per append. In the
/// closed-loop tests, each worker appends to a stream of its own until the stream holds
/// <see cref="DefaultEventsPerStream"/> events, unless the test sets another length, and then starts a new stream. The
/// open-loop test takes its streams from an <see cref="IdleStreamPool"/>. Results are printed to the test output. An
/// append test fails only if an append fails, and a read test fails only if it reads the wrong number of events.
/// </summary>
[Category("Benchmark")]
public abstract class EventStoreBenchmarkTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    /// <summary>
    /// The number of events a worker appends to a stream before it starts a new stream, unless a test sets another
    /// length.
    /// </summary>
    protected const int DefaultEventsPerStream = 10;

    private readonly EventTypeMap _eventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    // A benchmark's result must not depend on which tests ran before it.
    protected override bool ResetLogBeforeEachTest => true;

    /// <summary>
    /// Measures the latency of a single append with no concurrent appends.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_MeasureLatency(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore(_eventTypeMap);

        var streamIdGenerator = new StreamIdGenerator(workerCount: 1, DefaultEventsPerStream);
        var runner = new BenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            (workerIndex, token) => AppendAsync(eventStore, streamIdGenerator.Next(workerIndex).StreamId, token),
            new LatencyOptions(),
            ct);

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await Harness.DescribeAsync());
        await BenchmarkReport.WriteLatencyAsync(
            $"append latency, 1 in flight, 1 event per append, {DefaultEventsPerStream} events per stream", result);

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
    public Task AppendAsync_MeasureThroughput(int instanceCount, int inFlight, CancellationToken ct) =>
        MeasureAppendThroughputAsync(instanceCount, inFlight, DefaultEventsPerStream, nameCount: null, ct);

    /// <summary>
    /// Append throughput as a function of stream length, with one instance and 1,000 in flight. With one event per
    /// stream, every append takes the store's new-stream path.
    /// </summary>
    [TestCase(1)]
    [TestCase(10)]
    [TestCase(100)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public Task AppendAsync_MeasureThroughput_StreamLengthSweep(int eventsPerStream, CancellationToken ct) =>
        MeasureAppendThroughputAsync(instanceCount: 1, inFlight: 1_000, eventsPerStream, nameCount: null, ct);

    /// <summary>
    /// Append throughput as a function of how many names new streams draw from, with one instance and 1,000 in flight.
    /// Names are drawn at random, so the 1,000 streams being appended to at any moment have about 632 distinct names
    /// when there are 1,000, and about 952 when there are 10,000. With one name, streams sit together as in the other
    /// append tests, which gives the sweep a baseline from the same run.
    /// </summary>
    [TestCase(1)]
    [TestCase(100)]
    [TestCase(1_000)]
    [TestCase(10_000)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public Task AppendAsync_MeasureThroughput_StreamNameSweep(int nameCount, CancellationToken ct) =>
        MeasureAppendThroughputAsync(instanceCount: 1, inFlight: 1_000, DefaultEventsPerStream, nameCount, ct);

    /// <summary>
    /// Append throughput and latency in an open loop: appends arrive at random, spread over one or more store
    /// instances, at the rate that <see cref="OpenLoopOptions.RateEnvironmentVariable"/> sets, whatever the appends in
    /// flight do. Each append goes to a stream with no append in flight and expects the stream's version, so appends
    /// never conflict.
    /// </summary>
    [TestCase(1)]
    [TestCase(4)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_MeasureOpenLoop(int instanceCount, CancellationToken ct)
    {
        var options = OpenLoopOptions.FromEnvironment();
        var instances = CreateInstances(instanceCount);

        try
        {
            var streams = new IdleStreamPool(options.MaxInFlight, DefaultEventsPerStream);
            var runner = new BenchmarkRunner();

            var result = await runner.MeasureOpenLoopAsync(
                (index, token) => AppendToIdleStreamAsync(instances[index % instanceCount], streams, token),
                options,
                ct);

            await BenchmarkReport.WriteEnvironmentAsync(instances[0].GetType(), await Harness.DescribeAsync());

            await BenchmarkReport.WriteOpenLoopAsync(
                $"open-loop append, {instanceCount} instance(s), {options.Rate:N0}/s offered, " +
                $"1 event per append, {DefaultEventsPerStream} events per stream",
                result);

            result.Errors.ShouldBe(0, "an open-loop figure with failed appends is not meaningful");
        }
        finally
        {
            await DisposeAllAsync(instances);
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

    private async Task MeasureAppendThroughputAsync(
        int instanceCount,
        int inFlight,
        int eventsPerStream,
        int? nameCount,
        CancellationToken ct)
    {
        var instances = CreateInstances(instanceCount);

        try
        {
            var streamIdGenerator = new StreamIdGenerator(inFlight, eventsPerStream, nameCount);
            var runner = new BenchmarkRunner();

            var result = await runner.MeasureThroughputAsync(
                (workerIndex, token) => AppendAsync(
                    instances[workerIndex % instanceCount],
                    streamIdGenerator.Next(workerIndex).StreamId,
                    token),
                new ThroughputOptions { InFlight = inFlight },
                ct);

            await BenchmarkReport.WriteEnvironmentAsync(instances[0].GetType(), await Harness.DescribeAsync());

            var names = nameCount is { } count ? $", {count:N0} stream name(s)" : "";

            await BenchmarkReport.WriteThroughputAsync(
                $"append throughput, {instanceCount} instance(s), {inFlight:N0} in flight, " +
                $"1 event per append, {eventsPerStream:N0} event(s) per stream{names}",
                result);

            result.Errors.ShouldBe(0, "a throughput figure with failed appends is not meaningful");
            result.Completed.ShouldBeGreaterThan(0);
        }
        finally
        {
            await DisposeAllAsync(instances);
        }
    }

    private IEventStore<object, string, TStreamPos, TLogPos>[] CreateInstances(int instanceCount)
    {
        var instances = new IEventStore<object, string, TStreamPos, TLogPos>[instanceCount];
        for (var i = 0; i < instanceCount; i++)
            instances[i] = CreateEventStore(_eventTypeMap, loggerNameSuffix: $"_{i}");

        return instances;
    }

    private static async Task DisposeAllAsync(IEventStore<object, string, TStreamPos, TLogPos>[] instances)
    {
        foreach (var instance in instances)
            await instance.DisposeAsync();
    }

    private async Task AppendToIdleStreamAsync(
        IEventStore<object, string, TStreamPos, TLogPos> instance,
        IdleStreamPool streams,
        CancellationToken ct)
    {
        var lease = streams.Take();

        try
        {
            var expectedState = lease.ExpectedVersion is { } version
                ? ExpectedStreamState.AtVersion(CreateStreamPosition(version))
                : ExpectedStreamState<TStreamPos>.DoesNotExist;

            object[] events = [new TestEvent { Value = "Benchmark" }];
            await instance.AppendAsync(lease.StreamId, events, expectedState, cancellationToken: ct);
        }
        finally
        {
            streams.Return(lease);
        }
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
        await ReadBenchmarkReport.WriteAsync(title, result);

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