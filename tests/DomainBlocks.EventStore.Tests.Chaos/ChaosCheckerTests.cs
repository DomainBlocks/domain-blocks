using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Chaos;

[TestFixture]
public class ChaosCheckerTests
{
    [Test]
    public void Check_ConsistentRun_ReportsNoViolations()
    {
        var log = CreateLog("s1", "s2", "s1");

        Check(OkOperations(log), log).ShouldBeEmpty();
    }

    [Test]
    public void Check_LogPositionSkipped_ReportsLogGap()
    {
        ObservedEvent[] log = [.. CreateLog("s1", "s2", "s3").Where((_, i) => i != 1)];

        Check(OkOperations(log), log).ShouldBe([Invariant.LogIsContiguous]);
    }

    [Test]
    public void Check_StreamPositionSkipped_ReportsStreamGap()
    {
        var log = CreateLog("s1", "s1");
        ObservedEvent[] skippingLog = [log[0], log[1] with { StreamPosition = new StreamPosition(2) }];

        Check(OkOperations(log), skippingLog).ShouldBe([Invariant.StreamsAreContiguous]);
    }

    [Test]
    public void Check_EventWrittenTwice_ReportsDuplicate()
    {
        var log = CreateLog("s1", "s1");
        ObservedEvent[] duplicatingLog = [log[0], log[1] with { Operation = log[0].Operation }];

        Check(OkOperations(log[..1]), duplicatingLog).ShouldBe([Invariant.NoEventIsDuplicated]);
    }

    [Test]
    public void Check_AcknowledgedEventMissing_ReportsLostAppend()
    {
        var log = CreateLog("s1", "s1");

        Check(OkOperations(log), log[..1]).ShouldBe([Invariant.NoAcknowledgedAppendIsLost]);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Check_IndeterminateAppend_ReportsNoViolations(bool isWritten)
    {
        var log = CreateLog("s1", "s1");
        var indeterminate = new AppendOperation(1, "s1", 1, AppendOutcome.Indeterminate, ["TimeoutException"]);

        Check([.. OkOperations(log[..1]), indeterminate], isWritten ? log : log[..1]).ShouldBeEmpty();
    }

    [Test]
    public void Check_SubscriberSkippedEvent_ReportsDivergence()
    {
        var log = CreateLog("s1", "s2", "s3");
        ObservedEvent[] received = [log[0], log[2]];

        Check(OkOperations(log), log, received).ShouldBe([Invariant.SubscriberMatchesLog]);
    }

    [Test]
    public void Check_SubscriberReceivedEventAfterLogEnd_ReportsDivergence()
    {
        var log = CreateLog("s1", "s2");

        Check(OkOperations(log[..1]), log[..1], log).ShouldBe([Invariant.SubscriberMatchesLog]);
    }

    [Test]
    public void Check_SubscriberBehind_ReportsNotCaughtUp()
    {
        var log = CreateLog("s1", "s2");

        Check(OkOperations(log), log, log[..1]).ShouldBe([Invariant.SubscriberCaughtUp]);
    }

    [Test]
    public void Check_SubscriptionFailed_ReportsFailure()
    {
        var log = CreateLog("s1");

        Check(OkOperations(log), log, subscriptionError: new TimeoutException())
            .ShouldBe([Invariant.SubscriptionKeptRunning]);
    }

    private static Invariant[] Check(
        IReadOnlyList<AppendOperation> operations,
        IReadOnlyList<ObservedEvent> log,
        IReadOnlyList<ObservedEvent>? received = null,
        Exception? subscriptionError = null)
    {
        var run = new RunRecord(operations, log, received ?? log, subscriptionError, fellBehindCount: 0);
        return [.. ChaosChecker.Check(run).Violations.Select(x => x.Invariant)];
    }

    // Each event is the only one of its own operation.
    private static ObservedEvent[] CreateLog(params string[] streamIds)
    {
        var streamPositions = new Dictionary<string, ulong>();

        return
        [
            .. streamIds.Select((streamId, i) =>
            {
                var streamPosition = streamPositions.GetValueOrDefault(streamId);
                streamPositions[streamId] = streamPosition + 1;

                return new ObservedEvent(
                    new LogPosition((ulong)i + 1),
                    streamId,
                    new StreamPosition(streamPosition),
                    Operation: i,
                    Index: 0);
            })
        ];
    }

    private static AppendOperation[] OkOperations(IEnumerable<ObservedEvent> log)
    {
        return [.. log.Select(x => new AppendOperation(x.Operation, x.StreamId, 1, AppendOutcome.Ok, []))];
    }
}