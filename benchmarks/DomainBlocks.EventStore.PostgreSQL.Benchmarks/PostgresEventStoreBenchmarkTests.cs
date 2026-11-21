using System.Diagnostics;
using DomainBlocks.Benchmarking;
using DomainBlocks.Benchmarking.EventStore;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using HdrHistogram;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Benchmarks;

[TestFixture]
public class PostgresEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, LogPosition>(new PostgresEventStoreTestHarness())
{
    private static readonly EventTypeMap EventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    private PostgresEventStoreTestHarness Postgres => (PostgresEventStoreTestHarness)Harness;

    /// <summary>
    /// Append-to-observe latency for a live subscription: each sample appends one event and waits for it to arrive.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_MeasureLiveLatency(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore(EventTypeMap);

        await using var enumerator = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

        var streamIdGenerator = new StreamIdGenerator(workerCount: 1, DefaultEventsPerStream);
        var runner = new BenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            async (workerIndex, token) =>
            {
                await eventStore.AppendAsync(
                    streamIdGenerator.Next(workerIndex),
                    [new TestEvent { Value = "Benchmark" }],
                    cancellationToken: token);

                (await enumerator.MoveNextAsync()).ShouldBeTrue();
                enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.Event);
            },
            new LatencyOptions
            {
                WarmUp = TimeSpan.FromSeconds(1),
                MinWarmUpOperations = 10,
                SampleCount = 1_000,
                MaxDuration = TimeSpan.FromSeconds(30)
            },
            ct);

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await Harness.DescribeAsync());
        await BenchmarkReport.WriteLatencyAsync("append-to-observe latency, live SubscribeToAll, 1 in flight", result);

        result.Errors.ShouldBe(0);
        result.Latencies.TotalCount.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Throughput ceiling as a function of the maximum append batch size. Every batch serializes on the sequence row,
    /// so the ceiling is the batch size over the per-batch critical section. Larger batches amortize the fixed part of
    /// that section at the cost of latency under load. The queue capacity and in-flight count are at least the batch
    /// size so that batches can actually fill.
    /// </summary>
    [TestCase(500, 1_000)]
    [TestCase(1_000, 1_000)]
    [TestCase(500, 2_000)]
    [TestCase(1_000, 2_000)]
    [TestCase(2_000, 2_000)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_MeasureThroughput_BatchSizeSweep(int batchSize, int inFlight, CancellationToken ct)
    {
        var options = new PostgresEventStoreOptions
        {
            Schema = Postgres.Options.Schema,
            AppendMaxBatchSize = batchSize,
            AppendQueueCapacity = Math.Max(inFlight, Postgres.Options.AppendQueueCapacity)
        };

        await using var eventStore = Postgres
            .CreateBuilder()
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseOptions(options)
            .Build();

        // Server-side time inside append_events per batch, to separate the function from the rest of the cycle.
        await ExecuteAsync("ALTER SYSTEM SET track_functions = 'pl'; SELECT pg_reload_conf()");
        var (nextBefore, callsBefore, millisBefore) = await ReadAppendStatsAsync();
        var start = Stopwatch.GetTimestamp();

        var streamIdGenerator = new StreamIdGenerator(inFlight, DefaultEventsPerStream);
        var runner = new BenchmarkRunner();

        var result = await runner.MeasureThroughputAsync(
            (workerIndex, token) => eventStore.AppendAsync(
                streamIdGenerator.Next(workerIndex),
                [new TestEvent { Value = "Benchmark" }],
                cancellationToken: token),
            new ThroughputOptions { InFlight = inFlight },
            ct);

        var elapsed = Stopwatch.GetElapsedTime(start);
        var (nextAfter, callsAfter, millisAfter) = await ReadAppendStatsAsync();
        var batches = callsAfter - callsBefore;

        await BenchmarkReport.WriteEnvironmentAsync(
            eventStore.GetType(),
            await PostgresEventStoreTestHarness.DescribeAsync(options));

        await BenchmarkReport.WriteThroughputAsync(
            $"append throughput, batch size {batchSize:N0}, 1 instance, {inFlight:N0} in flight, " +
            $"1 event per append, {DefaultEventsPerStream} events per stream",
            result);

        if (batches > 0)
        {
            await TestContext.Out.WriteLineAsync(
                $"server:        {batches:N0} batches over warm-up and measurement, " +
                $"mean batch size {(nextAfter - nextBefore) / (double)batches:N1}, " +
                $"append_events {(millisAfter - millisBefore) / batches:F2} ms per batch, " +
                $"wall {elapsed.TotalMilliseconds / batches:F2} ms per batch");
        }

        result.Errors.ShouldBe(0, "a throughput figure with failed appends is not meaningful");
        result.Completed.ShouldBeGreaterThan(0);
    }

    private async Task<(long Next, long Calls, double TotalMillis)> ReadAppendStatsAsync()
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            $"SELECT s.next, coalesce(f.calls, 0), coalesce(f.total_time, 0) " +
            $"FROM {Postgres.Options.Schema}.sequences AS s " +
            "LEFT JOIN pg_stat_user_functions AS f ON f.schemaname = $1 AND f.funcname = 'append_events' " +
            "WHERE s.name = 'event_log'");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = Postgres.Options.Schema });

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();

        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetDouble(2));
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    // SPIKE: appends offered at a fixed rate from a spinning thread, independent of completions, spread over the
    // instances. Latency runs from each append's scheduled time, so queueing behind a slow append counts. Each of
    // 1,000 slots starts a new stream every 10 uses, as the closed-loop workers do.
    [TestCase(1, 1_000)]
    [TestCase(1, 10_000)]
    [TestCase(1, 30_000)]
    [TestCase(4, 1_000)]
    [TestCase(4, 10_000)]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task AppendAsync_SpikeOpenLoop(int instanceCount, int rate, CancellationToken ct)
    {
        var instances = new IEventStore<object, string, StreamPosition, LogPosition>[instanceCount];
        for (var i = 0; i < instanceCount; i++)
            instances[i] = CreateEventStore(EventTypeMap, loggerNameSuffix: $"_{i}");

        try
        {
            var runId = $"bench-{Guid.NewGuid():N}";

            var (opsPerSecond, errors, latencies) = await RunOpenLoopAsync(
                i => instances[i % instanceCount].AppendAsync(
                    $"{runId}-{i / 1_000 / DefaultEventsPerStream}-{i % 1_000}",
                    [new TestEvent { Value = "Benchmark" }],
                    cancellationToken: ct),
                rate,
                ct);

            var output = TestContext.Out;
            await output.WriteLineAsync($"--- open-loop append, {instanceCount} instance(s), {rate:N0}/s offered ---");
            await output.WriteLineAsync($"errors:        {errors:N0}");
            await output.WriteLineAsync($"throughput:    {opsPerSecond:N0} ops/s");
            await output.WriteLineAsync($"p50:           {Ms(latencies.GetValueAtPercentile(50)):F3} ms");
            await output.WriteLineAsync($"p90:           {Ms(latencies.GetValueAtPercentile(90)):F3} ms");
            await output.WriteLineAsync($"p99:           {Ms(latencies.GetValueAtPercentile(99)):F3} ms");
            await output.WriteLineAsync($"mean:          {Ms(latencies.GetMean()):F3} ms");
            await output.WriteLineAsync($"timer probe:   Task.Delay(1 ms) median {await ProbeTimerAsync(ct):F2} ms");

            errors.ShouldBe(0);
        }
        finally
        {
            foreach (var instance in instances)
                await instance.DisposeAsync();
        }

        static double Ms(double ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static async Task<double> ProbeTimerAsync(CancellationToken ct)
    {
        var samples = new double[21];

        for (var i = 0; i < samples.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            await Task.Delay(1, ct);
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static async Task<(double OpsPerSecond, long Errors, HdrHistogram.HistogramBase Latencies)>
        RunOpenLoopAsync(Func<long, Task> appendAsync, int rate, CancellationToken ct)
    {
        var warmUp = TimeSpan.FromSeconds(5);
        var measure = TimeSpan.FromSeconds(15);
        var freq = Stopwatch.Frequency;
        var latencies = new HdrHistogram.LongConcurrentHistogram(1, HdrHistogram.TimeStamp.Hours(1), 3);
        long errors = 0, completed = 0, outstanding = 0;

        var start = Stopwatch.GetTimestamp() + (freq / 10);
        var measureStart = start + (long)(warmUp.TotalSeconds * freq);
        var end = measureStart + (long)(measure.TotalSeconds * freq);
        var ticksPerOp = (double)freq / rate;

        async Task RunOneAsync(long index, long scheduled, bool measured)
        {
            try
            {
                await appendAsync(index);

                if (measured)
                {
                    latencies.RecordValue(Math.Max(1, Stopwatch.GetTimestamp() - scheduled));
                    Interlocked.Increment(ref completed);
                }
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
            finally
            {
                Interlocked.Decrement(ref outstanding);
            }
        }

        var thread = new Thread(() =>
        {
            for (long i = 0; ; i++)
            {
                var scheduled = start + (long)(i * ticksPerOp);
                if (scheduled >= end)
                    break;

                while (Stopwatch.GetTimestamp() < scheduled)
                    Thread.SpinWait(10);

                Interlocked.Increment(ref outstanding);
                _ = RunOneAsync(i, scheduled, scheduled >= measureStart);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        thread.Start();
        await Task.Run(thread.Join, ct);

        var drainDeadline = Stopwatch.GetTimestamp() + (60 * freq);
        while (Interlocked.Read(ref outstanding) > 0 && Stopwatch.GetTimestamp() < drainDeadline)
            await Task.Delay(50, ct);

        return (completed / measure.TotalSeconds, errors, latencies);
    }
}