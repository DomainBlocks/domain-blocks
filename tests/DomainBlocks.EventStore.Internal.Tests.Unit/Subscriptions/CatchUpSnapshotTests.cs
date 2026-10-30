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
        (await ReadAsync(FromStart, end: 7)).ShouldBe(["read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAsync_FromEnd_ReadsNothing()
    {
        (await ReadAsync(FromEnd, end: 7)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAsync_AfterPositionBeforeEnd_ReadsFromPositionToEnd()
    {
        (await ReadAsync(After(3), end: 7)).ShouldBe(["read from 3 to 7"]);
    }

    [TestCase(7)]
    [TestCase(9)]
    public async Task ReadAsync_AfterPositionAtOrPastEnd_ReadsNothing(long position)
    {
        (await ReadAsync(After(position), end: 7)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAsync_EmptySequence_ReadsNothing()
    {
        (await ReadAsync(FromStart, end: null)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAsync_ResumedBeforeEnd_ReadsFromResumePositionToEnd()
    {
        (await ReadAsync(FromStart, end: 7, after: LogPosition.FromInt64(3))).ShouldBe(["read from 3 to 7"]);
    }

    [Test]
    public async Task ReadAsync_ResumedAtEnd_ReadsNothing()
    {
        (await ReadAsync(FromStart, end: 7, after: LogPosition.FromInt64(7))).ShouldBeEmpty();
    }

    [Test]
    public void Position_FromStart_IsEnd()
    {
        CreateSnapshot(FromStart, end: 7).Position.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void Position_FromEnd_IsEnd()
    {
        CreateSnapshot(FromEnd, end: 7).Position.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void Position_AfterPositionBeforeEnd_IsEnd()
    {
        CreateSnapshot(After(3), end: 7).Position.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void Position_AfterPositionPastEnd_IsPosition()
    {
        CreateSnapshot(After(9), end: 7).Position.ShouldBe(LogPosition.FromInt64(9));
    }

    [Test]
    public void Position_EmptySequenceFromStartOrEnd_IsNull()
    {
        CreateSnapshot(FromStart, end: null).Position.ShouldBeNull();
        CreateSnapshot(FromEnd, end: null).Position.ShouldBeNull();
    }

    [Test]
    public void Position_EmptySequenceAfterPosition_IsPosition()
    {
        CreateSnapshot(After(3), end: null).Position.ShouldBe(LogPosition.FromInt64(3));
    }

    private static SubscriptionOrigin<LogPosition> After(long position) =>
        SubscriptionOrigin.After(LogPosition.FromInt64(position));

    private static TestCatchUpSnapshot CreateSnapshot(SubscriptionOrigin<LogPosition> origin, long? end) =>
        new(origin, end, []);

    private static async Task<List<string>> ReadAsync(
        SubscriptionOrigin<LogPosition> origin,
        long? end,
        LogPosition? after = null)
    {
        var calls = new List<string>();

        await foreach (var _ in new TestCatchUpSnapshot(origin, end, calls).ReadAsync(after, CancellationToken.None))
        {
        }

        return calls;
    }
}