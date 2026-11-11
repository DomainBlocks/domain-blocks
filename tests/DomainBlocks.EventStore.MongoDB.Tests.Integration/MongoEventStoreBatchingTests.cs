using DomainBlocks.Testing.Integration.EventStore;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreBatchingTests
{
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SameCommitIdInBatch_WritesOnce(CancellationToken ct)
    {
        await using var scenario = await BatchingScenario.StartAsync(ct);

        var (first, repeat) = await scenario.AppendTwiceInOneBatchAsync(
            "stream-1",
            ExpectedStreamState.Any<StreamPosition>(),
            Guid.NewGuid(),
            ct);

        await first;
        await repeat;
        (await scenario.CountEventsAsync("stream-1", ct)).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SameCommitIdInBatchAndFirstConflicts_ThrowsSameConflict(CancellationToken ct)
    {
        await using var scenario = await BatchingScenario.StartAsync(ct);
        await scenario.AppendAsync("stream-1", ExpectedStreamState.Any<StreamPosition>(), Guid.NewGuid(), ct);

        var (first, repeat) = await scenario.AppendTwiceInOneBatchAsync(
            "stream-1",
            ExpectedStreamState.DoesNotExist<StreamPosition>(),
            Guid.NewGuid(),
            ct);

        var conflict = await first.ShouldThrowAsync<StreamAppendConflictException<StreamPosition>>();
        (await repeat.ShouldThrowAsync<StreamAppendConflictException<StreamPosition>>()).ShouldBeSameAs(conflict);
        (await scenario.CountEventsAsync("stream-1", ct)).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_CommittedAndNullCommitIdsInBatch_WritesOnlyNull(CancellationToken ct)
    {
        await using var scenario = await BatchingScenario.StartAsync(ct);
        var commitId = Guid.NewGuid();
        await scenario.AppendAsync("stream-1", ExpectedStreamState.Any<StreamPosition>(), commitId, ct);

        var (withoutCommitId, repeat) = await scenario.AppendInOneBatchAsync(
            () => scenario.AppendAsync("stream-2", ExpectedStreamState.Any<StreamPosition>(), commitId: null, ct),
            () => scenario.AppendAsync("stream-1", ExpectedStreamState.Any<StreamPosition>(), commitId, ct),
            ct);

        await withoutCommitId;
        await repeat;
        (await scenario.CountEventsAsync("stream-1", ct)).ShouldBe(1);
        (await scenario.CountEventsAsync("stream-2", ct)).ShouldBe(1);
    }
}