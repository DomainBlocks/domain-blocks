using DomainBlocks.Testing.Integration.EventStore;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreBatchingTests : PostgresIntegrationTest
{
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAppends_GlobalPositionsAreGapFree(CancellationToken ct)
    {
        const int instanceCount = 3;
        const int appendsPerInstance = 100;
        const int streamCount = 10;

        var instances = Enumerable.Range(0, instanceCount).Select(i => CreateEventStore($"_gapfree-{i}")).ToArray();

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

        var rows = await Client.ReadRowsAsync();
        const int expectedCount = instanceCount * appendsPerInstance * 2;

        rows.Count.ShouldBe(expectedCount);
        rows.Select(x => x.Position).ShouldBe(Enumerable.Range(0, expectedCount).Select(i => (long)i));
        (await Client.GetSequenceNextAsync()).ShouldBe(expectedCount);

        foreach (var stream in rows.GroupBy(x => x.StreamId))
            stream.Select(x => x.StreamPosition).ShouldBe(Enumerable.Range(0, stream.Count()).Select(i => (long)i));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAppends_MixedOutcomesInBatch_CompleteIndividually(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore("_mixed", new PostgresEventStoreOptions
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

        var rows = await Client.ReadRowsAsync();
        rows.Count.ShouldBe(11);
        rows.Select(x => x.StreamPosition).ShouldBe(Enumerable.Range(0, 11).Select(i => (long)i));
        (await Client.GetSequenceNextAsync()).ShouldBe(11);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAppends_AreCoalescedIntoBatches(CancellationToken ct)
    {
        const int appendCount = 50;

        await using var eventStore = CreateEventStore("_coalesced", new PostgresEventStoreOptions
        {
            Schema = Schema,
            AppendBatchingDelay = TimeSpan.FromMilliseconds(50)
        });

        var tasks = Enumerable.Range(0, appendCount)
            .Select(i => eventStore.AppendAsync($"s{i}", [Appendable($"e{i}")], cancellationToken: ct));

        await Task.WhenAll(tasks);

        // Every row in a batch shares the batch's created_at, so the number of distinct timestamps is the number of
        // batches that were committed.
        var rows = await Client.ReadRowsAsync();
        var batchCount = rows.Select(x => x.CreatedAt).Distinct().Count();

        await TestContext.Out.WriteLineAsync($"{appendCount} appends were committed in {batchCount} batch(es)");

        rows.Count.ShouldBe(appendCount);
        batchCount.ShouldBeLessThan(appendCount);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task DisposeAsync_BatchInFlight_CommitsBatchAndFaultsQueuedRequests(CancellationToken ct)
    {
        var eventStore = CreateEventStore("_dispose");

        // Holding the sequence row keeps the first batch waiting inside the database, so the appends after it queue.
        await using var connection = await DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var lockCommand = new NpgsqlCommand($"SELECT next FROM {Schema}.sequences FOR UPDATE", connection))
        {
            await lockCommand.ExecuteNonQueryAsync(ct);
        }

        var inFlight = eventStore.AppendAsync("s0", [Appendable("e0")], cancellationToken: ct);
        await WaitForAppendLockWaitAsync(ct);

        var queued = Enumerable.Range(1, 4)
            .Select(i => eventStore.AppendAsync($"s{i}", [Appendable($"e{i}")], cancellationToken: ct))
            .ToArray();

        var dispose = eventStore.DisposeAsync().AsTask();

        await Task.Delay(200, ct);
        dispose.IsCompleted.ShouldBeFalse();
        inFlight.IsCompleted.ShouldBeFalse();

        await transaction.RollbackAsync(ct);

        await inFlight;
        await dispose;

        foreach (var task in queued)
            await Should.ThrowAsync<OperationCanceledException>(() => task);

        (await Client.ReadRowsAsync(cancellationToken: ct)).Select(x => x.StreamId).ShouldBe(["s0"]);
    }

    [Test]
    public async Task AppendAsync_AfterDispose_Throws()
    {
        var eventStore = CreateEventStore("_disposed");
        await eventStore.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => eventStore.AppendAsync("s1", [Appendable("a")]));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_LargestMaxBatchSize_Commits(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore("_largest", new PostgresEventStoreOptions
        {
            Schema = Schema,
            AppendMaxBatchSize = int.MaxValue
        });

        await eventStore.AppendAsync("s1", [Appendable("e")], cancellationToken: ct);

        (await Client.ReadRowsAsync(cancellationToken: ct)).Count.ShouldBe(1);
    }

    // Waits until an append batch on the fixture's schema waits for a lock, such as the sequence row's.
    private async Task WaitForAppendLockWaitAsync(CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity " +
            "WHERE wait_event_type = 'Lock' AND position($1 IN query) > 0)");

        command.Parameters.Add(new NpgsqlParameter<string>
        {
            TypedValue = new SchemaObjectNames(Schema).AppendEventsFunction
        });

        while (!(bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull())
            await Task.Delay(20, ct);
    }
}