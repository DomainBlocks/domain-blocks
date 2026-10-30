using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class CatchUpSnapshotTests
{
    private static readonly SubscriptionOrigin<LogPosition> FromStart = SubscriptionOrigin.Start;
    private static readonly SubscriptionOrigin<LogPosition> FromEnd = SubscriptionOrigin.End;

    [Test]
    public async Task ReadAsync_FromStart_ReadsUpToEnd()
    {
        (await ReadAsync(end: 7, FromStart)).ShouldBe(["read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAsync_FromEnd_ReadsNothing()
    {
        (await ReadAsync(end: 7, FromEnd)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAsync_AfterPositionBeforeEnd_ReadsFromPositionToEnd()
    {
        (await ReadAsync(end: 7, After(3))).ShouldBe(["read from 3 to 7"]);
    }

    [TestCase(7)]
    [TestCase(9)]
    public async Task ReadAsync_AfterPositionAtOrPastEnd_ReadsNothing(long position)
    {
        (await ReadAsync(end: 7, After(position))).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAsync_EmptySequence_ReadsNothing()
    {
        (await ReadAsync(end: null, FromStart)).ShouldBeEmpty();
    }

    [Test]
    public void GetResumePosition_FromStart_ReturnsEnd()
    {
        CreateSnapshot(end: 7).GetResumePosition(FromStart).ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void GetResumePosition_FromEnd_ReturnsEnd()
    {
        CreateSnapshot(end: 7).GetResumePosition(FromEnd).ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void GetResumePosition_AfterPositionBeforeEnd_ReturnsEnd()
    {
        CreateSnapshot(end: 7).GetResumePosition(After(3)).ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void GetResumePosition_AfterPositionPastEnd_ReturnsPosition()
    {
        CreateSnapshot(end: 7).GetResumePosition(After(9)).ShouldBe(LogPosition.FromInt64(9));
    }

    [Test]
    public void GetResumePosition_EmptySequenceFromStartOrEnd_ReturnsNull()
    {
        CreateSnapshot(end: null).GetResumePosition(FromStart).ShouldBeNull();
        CreateSnapshot(end: null).GetResumePosition(FromEnd).ShouldBeNull();
    }

    [Test]
    public void GetResumePosition_EmptySequenceAfterPosition_ReturnsPosition()
    {
        CreateSnapshot(end: null).GetResumePosition(After(3)).ShouldBe(LogPosition.FromInt64(3));
    }

    private static SubscriptionOrigin<LogPosition> After(long position) =>
        SubscriptionOrigin.After(LogPosition.FromInt64(position));

    private static TestCatchUpSnapshot CreateSnapshot(long? end) => new(end, []);

    private static async Task<List<string>> ReadAsync(long? end, SubscriptionOrigin<LogPosition> origin)
    {
        var calls = new List<string>();

        await foreach (var _ in new TestCatchUpSnapshot(end, calls).ReadAsync(origin, CancellationToken.None))
        {
        }

        return calls;
    }
}