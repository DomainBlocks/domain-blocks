using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreBatchingTests
{
    private const string Schema = "dbx_es_batching_tests";
    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema };
    private static readonly EventTypeMap EventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    private AppendFunctionClient _client = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, Options);
        _client = new AppendFunctionClient(SetUpFixture.DataSource, Schema);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, Options);
    }

    [SetUp]
    public async Task SetUp()
    {
        await _client.ResetAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAppends_GlobalPositionsAreGapFree(CancellationToken ct)
    {
        const int instanceCount = 3;
        const int appendsPerInstance = 100;
        const int streamCount = 10;

        var instances = Enumerable.Range(0, instanceCount).Select(i => CreateEventStore($"gapfree-{i}")).ToArray();

        try
        {
            var tasks = instances.SelectMany((instance, i) => Enumerable
                .Range(0, appendsPerInstance)
                .Select(j => instance.AppendAsync(
                    $"s{j % streamCount}",
                    [Appendable($"i{i}-e{j}"), Appendable($"i{i}-e{j}-b")],
                    cancellationToken: ct)));

            await Task.WhenAll(tasks);
        }
        finally
        {
            foreach (var instance in instances)
                await instance.DisposeAsync();
        }

        var rows = await _client.ReadRowsAsync();
        const int expectedCount = instanceCount * appendsPerInstance * 2;

        rows.Count.ShouldBe(expectedCount);
        rows.Select(x => x.Position).ShouldBe(Enumerable.Range(0, expectedCount).Select(i => (long)i));
        (await _client.GetSequenceNextAsync()).ShouldBe(expectedCount);

        foreach (var stream in rows.GroupBy(x => x.StreamId))
            stream.Select(x => x.StreamPosition).ShouldBe(Enumerable.Range(0, stream.Count()).Select(i => (long)i));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAppends_MixedOutcomesInBatch_CompleteIndividually(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore("mixed", new PostgresEventStoreOptions
        {
            Schema = Schema,
            AppendBatchingDelay = TimeSpan.FromMilliseconds(50)
        });

        await eventStore.AppendAsync("existing", [Appendable("seed")], cancellationToken: ct);

        var anyTasks = Enumerable.Range(0, 10)
            .Select(i => eventStore.AppendAsync("existing", [Appendable($"any-{i}")], cancellationToken: ct))
            .ToArray();

        var createTasks = Enumerable.Range(0, 10)
            .Select(_ => eventStore.AppendAsync(
                "existing",
                [Appendable("create")],
                ExpectedStreamState.DoesNotExist<StreamPosition>(),
                cancellationToken: ct))
            .ToArray();

        var staleTasks = Enumerable.Range(0, 10)
            .Select(_ => eventStore.AppendAsync(
                "existing",
                [Appendable("stale")],
                ExpectedStreamState.AtVersion(new StreamPosition(99)),
                cancellationToken: ct))
            .ToArray();

        await Task.WhenAll(anyTasks);

        foreach (var task in createTasks.Concat(staleTasks))
            await Should.ThrowAsync<StreamAppendConflictException<StreamPosition>>(() => task);

        var rows = await _client.ReadRowsAsync();
        rows.Count.ShouldBe(11);
        rows.Select(x => x.StreamPosition).ShouldBe(Enumerable.Range(0, 11).Select(i => (long)i));
        (await _client.GetSequenceNextAsync()).ShouldBe(11);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAppends_AreCoalescedIntoBatches(CancellationToken ct)
    {
        const int appendCount = 50;

        await using var eventStore = CreateEventStore("coalesced", new PostgresEventStoreOptions
        {
            Schema = Schema,
            AppendBatchingDelay = TimeSpan.FromMilliseconds(50)
        });

        var tasks = Enumerable.Range(0, appendCount)
            .Select(i => eventStore.AppendAsync($"s{i}", [Appendable($"e{i}")], cancellationToken: ct));

        await Task.WhenAll(tasks);

        // Every row in a batch shares the batch's created_at, so the number of distinct timestamps is the number of
        // batches that were committed.
        var rows = await _client.ReadRowsAsync();
        var batchCount = rows.Select(x => x.CreatedAt).Distinct().Count();

        TestContext.Out.WriteLine($"{appendCount} appends were committed in {batchCount} batch(es)");

        rows.Count.ShouldBe(appendCount);
        batchCount.ShouldBeLessThan(appendCount);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task DisposeAsync_WithQueuedRequests_FaultsThem(CancellationToken ct)
    {
        var eventStore = CreateEventStore("dispose");

        // Hold the sequence row so that the appender's batch blocks inside the database.
        await using var connection = await SetUpFixture.DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var lockCommand = new NpgsqlCommand($"SELECT next FROM {Schema}.sequences FOR UPDATE", connection))
        {
            await lockCommand.ExecuteNonQueryAsync(ct);
        }

        var tasks = Enumerable.Range(0, 5)
            .Select(i => eventStore.AppendAsync($"s{i}", [Appendable($"e{i}")], cancellationToken: ct))
            .ToArray();

        await Task.Delay(200, ct);
        tasks.ShouldAllBe(x => !x.IsCompleted);

        await eventStore.DisposeAsync();

        foreach (var task in tasks)
            await Should.ThrowAsync<OperationCanceledException>(() => task);

        await transaction.RollbackAsync(ct);

        (await _client.ReadRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task AppendAsync_AfterDispose_Throws()
    {
        var eventStore = CreateEventStore("disposed");
        await eventStore.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => eventStore.AppendAsync("s1", [Appendable("a")]));
    }

    private static AppendableEvent<object> Appendable(string value)
    {
        return AppendableEvent.Create<object>(new TestEvent { Value = value });
    }

    private static PostgresEventStore<object> CreateEventStore(string name, PostgresEventStoreOptions? options = null)
    {
        return PostgresEventStore.Create(
            SetUpFixture.DataSource,
            TestPostgresEventCodec.Create<object>(EventTypeMap),
            options ?? Options,
            SetUpFixture.LoggerFactory.CreateLogger($"PostgresEventStore_{name}"));
    }
}
