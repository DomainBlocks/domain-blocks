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
}