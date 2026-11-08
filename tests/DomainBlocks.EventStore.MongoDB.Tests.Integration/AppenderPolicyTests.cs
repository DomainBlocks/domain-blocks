using DomainBlocks.Testing.Integration.EventStore;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class AppenderPolicyTests
{
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task OnBatchCommittingAsync_SameCommitIdInBatch_FailsRepeat(CancellationToken ct)
    {
        await using var scenario = await AppenderPolicyScenario.StartAsync(ct);
        var commitId = Guid.NewGuid();

        var (first, repeat) = await scenario.AppendTwiceInOneBatchAsync(
            "stream-1",
            ExpectedStreamState.Any<StreamPosition>(),
            commitId,
            ct);

        await first;
        (await repeat.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain(commitId.ToString());
        (await scenario.CountEventsAsync("stream-1", ct)).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task OnBatchCommittingAsync_SameCommitIdInBatchAndFirstConflicts_FailsRepeat(CancellationToken ct)
    {
        await using var scenario = await AppenderPolicyScenario.StartAsync(ct);
        await scenario.AppendAsync("stream-1", ExpectedStreamState.Any<StreamPosition>(), Guid.NewGuid(), ct);
        var commitId = Guid.NewGuid();

        var (first, repeat) = await scenario.AppendTwiceInOneBatchAsync(
            "stream-1",
            ExpectedStreamState.DoesNotExist<StreamPosition>(),
            commitId,
            ct);

        await first.ShouldThrowAsync<StreamAppendConflictException<StreamPosition>>();
        (await repeat.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain(commitId.ToString());
        (await scenario.CountEventsAsync("stream-1", ct)).ShouldBe(1);
    }
}