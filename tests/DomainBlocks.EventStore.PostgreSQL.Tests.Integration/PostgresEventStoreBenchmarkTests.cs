using System.Diagnostics;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreBenchmarkTests : EventStoreBenchmarkTests<StreamPosition, LogPosition>
{
    private PostgresEventStoreOptions _options = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _options = new PostgresEventStoreOptions { Schema = "dbx_es_benchmark_tests" };
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, _options);
    }

    [Test]
    [Explicit("Benchmark")]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_MeasureLiveLatency(CancellationToken ct)
    {
        const int warmupIterations = 10;
        const int iterations = 100;

        var eventStore = CreateEventStore(EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>()));

        try
        {
            await using var enumerator = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);

            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            enumerator.Current.ShouldBeOfType<SubscriptionMessage.CaughtUp>();

            var latencies = new List<double>(iterations);

            for (var i = 0; i < warmupIterations + iterations; i++)
            {
                var sw = Stopwatch.StartNew();

                await eventStore.AppendAsync(
                    $"test-{Guid.NewGuid():N}",
                    [new TestEvent { Value = "Benchmark" }],
                    cancellationToken: ct);

                (await enumerator.MoveNextAsync()).ShouldBeTrue();
                enumerator.Current.ShouldBeOfType<
                    SubscriptionMessage.Event<ReadEvent<object, string, StreamPosition, LogPosition>>>();

                sw.Stop();

                if (i >= warmupIterations)
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
            }

            var sorted = latencies.OrderBy(x => x).ToList();
            await TestContext.Out.WriteLineAsync("append-to-observe latency");
            await TestContext.Out.WriteLineAsync($"p50:  {sorted[Percentile(0.50)]:F1} ms");
            await TestContext.Out.WriteLineAsync($"p90:  {sorted[Percentile(0.90)]:F1} ms");
            await TestContext.Out.WriteLineAsync($"p99:  {sorted[Percentile(0.99)]:F1} ms");
            await TestContext.Out.WriteLineAsync($"min:  {sorted[0]:F1} ms");
            await TestContext.Out.WriteLineAsync($"max:  {sorted[^1]:F1} ms");
            await TestContext.Out.WriteLineAsync($"mean: {latencies.Average():F1} ms");

            int Percentile(double p) => (int)Math.Ceiling(sorted.Count * p) - 1;
        }
        finally
        {
            if (eventStore is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
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
}