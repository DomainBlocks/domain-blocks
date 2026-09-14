using System.Diagnostics;
using DomainBlocks.Benchmarking;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing;
using DomainBlocks.Testing.Integration.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Benchmarks;

[TestFixture]
public class PostgresEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, LogPosition>(new PostgresEventStoreTestHarness())
{
    private static readonly EventTypeMap EventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    private PostgresEventStoreTestHarness Postgres => (PostgresEventStoreTestHarness)Harness;

    /// <summary>
    /// Append-to-observe latency for a live subscription: each sample appends one event and waits for it to arrive.
    /// On the default server configuration this tracks <c>wal_writer_delay</c>, so the report includes it.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(BenchmarkTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_MeasureLiveLatency(CancellationToken ct)
    {
        var eventStore = CreateEventStore(EventTypeMap);
        await using var disposable = eventStore as IAsyncDisposable;

        await using var enumerator = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.ShouldBeOfType<SubscriptionMessage.CaughtUp>();

        var runner = new AppendBenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            async (_, streamId, token) =>
            {
                await eventStore.AppendAsync(streamId, [new TestEvent { Value = "Benchmark" }],
                    cancellationToken: token);

                (await enumerator.MoveNextAsync()).ShouldBeTrue();
                enumerator.Current.ShouldBeOfType<
                    SubscriptionMessage.Event<ReadEvent<object, string, StreamPosition, LogPosition>>>();
            },
            new LatencyOptions
            {
                // Each sample costs a WAL writer cycle (200 ms by default), so bound by time rather than sample count.
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
    /// Throughput ceiling as a function of the append batch size. Every batch serialises on the sequence row, so the
    /// ceiling is the batch size over the per-batch critical section: larger batches amortise the fixed part of that
    /// section at the cost of latency under load. The queue capacity and in-flight count are at least the batch size
    /// so that batches can actually fill.
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
            AppendBatchSize = batchSize,
            AppendQueueCapacity = Math.Max(inFlight, Postgres.Options.AppendQueueCapacity)
        };

        await using var eventStore =
            Postgres.CreateEventStore(TestPostgresEventCodec.Create<object>(EventTypeMap), options);

        // Server-side time inside append_events per batch, to separate the function from the rest of the cycle.
        await ExecuteAsync("ALTER SYSTEM SET track_functions = 'pl'; SELECT pg_reload_conf()");
        var (nextBefore, callsBefore, millisBefore) = await ReadAppendStatsAsync();
        var start = Stopwatch.GetTimestamp();

        var runner = new AppendBenchmarkRunner();

        var result = await runner.MeasureThroughputAsync(
            (_, streamId, token) =>
                eventStore.AppendAsync(streamId, [new TestEvent { Value = "Benchmark" }], cancellationToken: token),
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
            "1 event per append, new stream per append",
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
}