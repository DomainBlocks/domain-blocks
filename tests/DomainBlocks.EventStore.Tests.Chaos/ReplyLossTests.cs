using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Tests that the MongoDB event store keeps every invariant while the server's replies are repeatedly lost.
/// </summary>
[TestFixture]
[Explicit("Runs Docker containers and injects faults for about a minute per test.")]
public class ReplyLossTests
{
    private const int TimeoutMillis = 5 * 60 * 1_000;

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task EventStore_RepliesLost_KeepsEveryInvariant(CancellationToken ct)
    {
        await using var scenario = await ReplyLossScenario.StartAsync(ct);

        var report = ChaosChecker.Check(await scenario.RunAsync(RetryCommitId.Same, ct));
        await TestContext.Out.WriteLineAsync(report.ToString());

        report.RetriedOperationCount.ShouldBePositive();
        report.Violations.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task EventStore_RetriesWithNewCommitId_DuplicatesEvents(CancellationToken ct)
    {
        await using var scenario = await ReplyLossScenario.StartAsync(ct);

        var report = ChaosChecker.Check(await scenario.RunAsync(RetryCommitId.New, ct));
        await TestContext.Out.WriteLineAsync(report.ToString());

        // A retry with a new commit ID writes the append again whenever the first attempt was written too. Without a
        // duplicate, the faults left no append in doubt, and the run with the same commit ID proves nothing.
        report.Violations.ShouldContain(x => x.Invariant == Invariant.NoEventIsDuplicated);
    }
}