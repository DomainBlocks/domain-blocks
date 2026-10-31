using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;
using NUnit.Framework;
using Polly;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class CatchUpSnapshotTests
{
    private static readonly SubscriptionOrigin<LogPosition> FromStart = SubscriptionOrigin.Start;
    private static readonly SubscriptionOrigin<LogPosition> FromEnd = SubscriptionOrigin.End;

    [Test]
    public async Task ReadAllAsync_FromStart_ReadsUpToEnd()
    {
        (await ReadAsync(FromStart, end: 7)).ShouldBe(["read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAllAsync_FromEnd_ReadsNothing()
    {
        (await ReadAsync(FromEnd, end: 7)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAllAsync_AfterPositionBeforeEnd_ReadsFromPositionToEnd()
    {
        (await ReadAsync(After(3), end: 7)).ShouldBe(["read from 3 to 7"]);
    }

    [TestCase(7)]
    [TestCase(9)]
    public async Task ReadAllAsync_AfterPositionAtOrPastEnd_ReadsNothing(long position)
    {
        (await ReadAsync(After(position), end: 7)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAllAsync_EmptySequence_ReadsNothing()
    {
        (await ReadAsync(FromStart, end: null)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAllAsync_ReadTwice_ReadsSameEventsEachTime()
    {
        var calls = new List<string>();
        var snapshot = new TestCatchUpSnapshot(FromStart, 7, ResiliencePipeline.Empty, calls, [1, 7]);

        (await ReadPositionsAsync(snapshot)).ShouldBe([1, 7]);
        (await ReadPositionsAsync(snapshot)).ShouldBe([1, 7]);

        calls.ShouldBe(["read from -1 to 7", "read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAllAsync_TransientErrorBeforeEnd_ReadsAgainAfterLastEvent()
    {
        var calls = new List<string>();
        var snapshot = new FailingSnapshot(end: 7, failAfterPosition: 3, CreateRetryPolicy(), calls);

        (await ReadPositionsAsync(snapshot)).ShouldBe([3]);

        calls.ShouldBe(["read from -1 to 7", "read from 3 to 7"]);
    }

    [Test]
    public async Task ReadAllAsync_TransientErrorAtEnd_DoesNotReadAgain()
    {
        var calls = new List<string>();
        var snapshot = new FailingSnapshot(end: 7, failAfterPosition: 7, CreateRetryPolicy(), calls);

        (await ReadPositionsAsync(snapshot)).ShouldBe([7]);

        calls.ShouldBe(["read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAllAsync_TransientErrorWithoutRetries_Throws()
    {
        var snapshot = new FailingSnapshot(end: 7, failAfterPosition: 3, ResiliencePipeline.Empty, []);

        await Should.ThrowAsync<IOException>(() => ReadPositionsAsync(snapshot));
    }

    [Test]
    public void CaughtUpPosition_FromStart_IsEnd()
    {
        CreateSnapshot(FromStart, end: 7).CaughtUpPosition.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void CaughtUpPosition_FromEnd_IsEnd()
    {
        CreateSnapshot(FromEnd, end: 7).CaughtUpPosition.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void CaughtUpPosition_AfterPositionBeforeEnd_IsEnd()
    {
        CreateSnapshot(After(3), end: 7).CaughtUpPosition.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void CaughtUpPosition_AfterPositionPastEnd_IsPosition()
    {
        CreateSnapshot(After(9), end: 7).CaughtUpPosition.ShouldBe(LogPosition.FromInt64(9));
    }

    [Test]
    public void CaughtUpPosition_EmptySequenceFromStartOrEnd_IsNull()
    {
        CreateSnapshot(FromStart, end: null).CaughtUpPosition.ShouldBeNull();
        CreateSnapshot(FromEnd, end: null).CaughtUpPosition.ShouldBeNull();
    }

    [Test]
    public void CaughtUpPosition_EmptySequenceAfterPosition_IsPosition()
    {
        CreateSnapshot(After(3), end: null).CaughtUpPosition.ShouldBe(LogPosition.FromInt64(3));
    }

    private static SubscriptionOrigin<LogPosition> After(long position) =>
        SubscriptionOrigin.After(LogPosition.FromInt64(position));

    private static TestCatchUpSnapshot CreateSnapshot(
        SubscriptionOrigin<LogPosition> origin,
        long? end,
        List<string>? calls = null)
    {
        return new TestCatchUpSnapshot(origin, end, ResiliencePipeline.Empty, calls ?? []);
    }

    // Retries at once.
    private static ResiliencePipeline CreateRetryPolicy()
    {
        return RetryPipeline.Create(
            static ex => ex is IOException,
            maxRetryAttempts: 1,
            Timeout.InfiniteTimeSpan,
            static (_, _, _, _) => { },
            delay: TimeSpan.Zero,
            maxDelay: TimeSpan.Zero);
    }

    private static async Task<List<string>> ReadAsync(SubscriptionOrigin<LogPosition> origin, long? end)
    {
        var calls = new List<string>();

        await foreach (var _ in CreateSnapshot(origin, end, calls).ReadAllAsync())
        {
        }

        return calls;
    }

    private static async Task<List<long>> ReadPositionsAsync(CatchUpSnapshot<string, LogPosition> snapshot)
    {
        var positions = new List<long>();

        await foreach (var e in snapshot.ReadAllAsync())
            positions.Add(e.Context.LogPosition.ToInt64());

        return positions;
    }

    /// <summary>
    /// Stands in for a store's snapshot whose first read delivers one event and then fails with a transient error, and
    /// whose later reads return no events.
    /// </summary>
    private sealed class FailingSnapshot(
        long end,
        long failAfterPosition,
        ResiliencePipeline retryPolicy,
        List<string> calls) :
        CatchUpSnapshot<string, LogPosition>(FromStart, end, retryPolicy)
    {
        private bool _hasFailed;

        protected override async IAsyncEnumerable<ReadEvent<string, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            calls.Add($"read from {startExclusive} to {endInclusive}");
            await Task.CompletedTask;

            if (_hasFailed)
                yield break;

            yield return TestCatchUpSnapshot.CreateEvent(failAfterPosition);

            _hasFailed = true;
            throw new IOException("The read failed.");
        }
    }
}