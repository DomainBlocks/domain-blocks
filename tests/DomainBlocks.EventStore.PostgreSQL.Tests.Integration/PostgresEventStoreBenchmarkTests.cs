using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.Benchmarking;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreBenchmarkTests : EventStoreBenchmarkTests<StreamPosition, LogPosition>
{
    private readonly PostgresEventStoreOptions _options = new() { Schema = "dbx_es_benchmark_tests" };

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, _options);
    }

    /// <summary>
    /// Append-to-observe latency for a live subscription: each sample appends one event and waits for it to arrive.
    /// On the default server configuration this tracks <c>wal_writer_delay</c>, so the report includes it.
    /// </summary>
    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.BenchmarkMillis)]
    public async Task SubscribeToAll_MeasureLiveLatency(CancellationToken ct)
    {
        var eventStore = CreateEventStore(EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>()));
        await using var disposable = eventStore as IAsyncDisposable;

        await using var enumerator = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.ShouldBeOfType<SubscriptionMessage.CaughtUp>();

        var runner = new AppendBenchmarkRunner();

        var result = await runner.MeasureLatencyAsync(
            async (_, streamId, token) =>
            {
                await eventStore.AppendAsync(streamId, [new TestEvent { Value = "Benchmark" }], cancellationToken: token);

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

        await BenchmarkReport.WriteEnvironmentAsync(eventStore.GetType(), await DescribeStoreAsync());
        await BenchmarkReport.WriteLatencyAsync("append-to-observe latency, live SubscribeToAll, 1 in flight", result);

        result.Errors.ShouldBe(0);
        result.Latencies.Count.ShouldBeGreaterThan(0);
    }

    protected override async Task ResetStoreAsync()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, _options);
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);
    }

    protected override async Task<string?> DescribeStoreAsync()
    {
        var version = await ShowAsync("server_version");
        var walWriterDelay = await ShowAsync("wal_writer_delay");
        var synchronousCommit = await ShowAsync("synchronous_commit");
        var fsync = await ShowAsync("fsync");
        var sharedBuffers = await ShowAsync("shared_buffers");

        return
            $"PostgresEventStore: schema {_options.Schema}, append batch size {_options.AppendBatchSize}, " +
            $"append queue capacity {_options.AppendQueueCapacity}, batching delay {_options.AppendBatchingDelay} " +
            $"(min count {_options.AppendBatchingDelayMinCount}); " +
            $"server {version}: wal_writer_delay {walWriterDelay}, synchronous_commit {synchronousCommit}, " +
            $"fsync {fsync}, shared_buffers {sharedBuffers}";
    }

    protected override IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        var eventCodec = TestPostgresEventCodec.Create(eventTypeMap, eventFormat, contractMappers);

        return PostgresEventStore.Create(
            SetUpFixture.DataSource,
            eventCodec,
            _options,
            SetUpFixture.LoggerFactory.CreateLogger($"PostgresEventStore{loggerNameSuffix}"));
    }

    private static async Task<string> ShowAsync(string setting)
    {
        await using var command = SetUpFixture.DataSource.CreateCommand($"SHOW {setting}");
        return (await command.ExecuteScalarAsync())?.ToString() ?? "?";
    }
}
