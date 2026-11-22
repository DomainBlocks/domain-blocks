using DomainBlocks.Testing.Integration.EventStore;
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
        await using var eventStore = CreateEventStore("_mixed");
        await eventStore.AppendAsync("existing", [Appendable("seed")], cancellationToken: ct);

        await using var held = await HeldBatch.StartAsync(eventStore, DataSource, Schema, ct);

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

        await held.ReleaseAsync(ct);
        await Task.WhenAll(anyTasks);

        foreach (var task in createTasks.Concat(staleTasks))
            await Should.ThrowAsync<StreamAppendConflictException<StreamPosition>>(() => task);

        var rows = (await Client.ReadRowsAsync()).Where(x => x.StreamId == "existing").ToList();
        rows.Count.ShouldBe(11);
        rows.Select(x => x.StreamPosition).ShouldBe(Enumerable.Range(0, 11).Select(i => (long)i));
        (await Client.GetSequenceNextAsync()).ShouldBe(12);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ConcurrentAppends_QueuedBehindBatch_CommitInOneBatch(CancellationToken ct)
    {
        const int appendCount = 50;

        await using var eventStore = CreateEventStore("_coalesced");
        await using var held = await HeldBatch.StartAsync(eventStore, DataSource, Schema, ct);

        var tasks = Enumerable.Range(0, appendCount)
            .Select(i => eventStore.AppendAsync($"s{i}", [Appendable($"e{i}")], cancellationToken: ct))
            .ToArray();

        await held.ReleaseAsync(ct);
        await Task.WhenAll(tasks);

        // Every row in a batch shares the batch's created_at.
        var rows = (await Client.ReadRowsAsync()).Where(x => x.StreamId != HeldBatch.StreamId).ToList();
        rows.Count.ShouldBe(appendCount);
        rows.Select(x => x.CreatedAt).Distinct().Count().ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task DisposeAsync_WithQueuedRequests_FaultsThem(CancellationToken ct)
    {
        var eventStore = CreateEventStore("_dispose");

        // Hold the sequence row so that the appender's batch blocks inside the database.
        await using var rowLock = await SequenceRowLock.AcquireAsync(DataSource, Schema, ct);

        var tasks = Enumerable.Range(0, 5)
            .Select(i => eventStore.AppendAsync($"s{i}", [Appendable($"e{i}")], cancellationToken: ct))
            .ToArray();

        await Task.Delay(200, ct);
        tasks.ShouldAllBe(x => !x.IsCompleted);

        await eventStore.DisposeAsync();

        foreach (var task in tasks)
            await Should.ThrowAsync<OperationCanceledException>(() => task);

        await rowLock.ReleaseAsync(ct);

        (await Client.ReadRowsAsync(cancellationToken: ct)).ShouldBeEmpty();
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

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SameCommitIdInBatch_WritesOnce(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore("_repeat");

        var (first, repeat) = await AppendTwiceInOneBatchAsync(
            eventStore,
            "s1",
            ExpectedStreamState.Any<StreamPosition>(),
            Guid.NewGuid(),
            ct);

        await first;
        await repeat;
        (await Client.ReadRowsAsync(cancellationToken: ct)).Count(x => x.StreamId == "s1").ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SameCommitIdInBatchAndFirstConflicts_ThrowsSameConflict(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore("_repeat-conflict");
        await eventStore.AppendAsync("s1", [Appendable("existing")], cancellationToken: ct);

        var (first, repeat) = await AppendTwiceInOneBatchAsync(
            eventStore,
            "s1",
            ExpectedStreamState.DoesNotExist<StreamPosition>(),
            Guid.NewGuid(),
            ct);

        var conflict = await first.ShouldThrowAsync<StreamAppendConflictException<StreamPosition>>();
        (await repeat.ShouldThrowAsync<StreamAppendConflictException<StreamPosition>>()).ShouldBeSameAs(conflict);
        (await Client.ReadRowsAsync(cancellationToken: ct)).Count(x => x.StreamId == "s1").ShouldBe(1);
    }

    // Makes the same append twice in one batch: both queue up behind a held batch.
    private async Task<(Task First, Task Repeat)> AppendTwiceInOneBatchAsync(
        IEventStore<object, string, StreamPosition, LogPosition> eventStore,
        string streamId,
        ExpectedStreamState<StreamPosition> expectedState,
        Guid commitId,
        CancellationToken ct)
    {
        await using var held = await HeldBatch.StartAsync(eventStore, DataSource, Schema, ct);

        AppendableEvent<object>[] events = [Appendable("e")];
        var first = eventStore.AppendAsync(streamId, events, expectedState, commitId, cancellationToken: ct);
        var repeat = eventStore.AppendAsync(streamId, events, expectedState, commitId, cancellationToken: ct);

        await held.ReleaseAsync(ct);
        return (first, repeat);
    }
}