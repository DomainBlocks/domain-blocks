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
    public async Task ReadAsync_TransientErrorBeforeEnd_ReadsAgainAfterLastEvent()
    {
        var calls = new List<string>();
        var history = new FailingHistorySnapshot(end: 7, failAfterPosition: 3, calls);

        var events = await ReadAllAsync(history, CreateRetryPipeline());

        events.ShouldBe([3]);
        calls.ShouldBe(["read from -1 to 7", "read from 3 to 7"]);
    }

    [Test]
    public async Task ReadAsync_TransientErrorAtEnd_DoesNotReadAgain()
    {
        var calls = new List<string>();
        var history = new FailingHistorySnapshot(end: 7, failAfterPosition: 7, calls);

        var events = await ReadAllAsync(history, CreateRetryPipeline());

        events.ShouldBe([7]);
        calls.ShouldBe(["read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAsync_TransientErrorWithoutRetries_Throws()
    {
        var history = new FailingHistorySnapshot(end: 7, failAfterPosition: 3, []);

        await Should.ThrowAsync<IOException>(() => ReadAllAsync(history, ResiliencePipeline.Empty));
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

    private static CatchUpSnapshot<string, LogPosition> CreateSnapshot(
        SubscriptionOrigin<LogPosition> origin,
        long? end,
        List<string>? calls = null)
    {
        return new CatchUpSnapshot<string, LogPosition>(
            new TestHistorySnapshot(end, calls ?? []),
            origin,
            static ctx => ctx.LogPosition,
            ResiliencePipeline.Empty);
    }

    // Retries at once.
    private static ResiliencePipeline CreateRetryPipeline()
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

        await foreach (var _ in CreateSnapshot(origin, end, calls).ReadAsync())
        {
        }

        return calls;
    }

    private static async Task<List<long>> ReadAllAsync(
        IHistorySnapshot<string> history,
        ResiliencePipeline retryPipeline)
    {
        var snapshot = new CatchUpSnapshot<string, LogPosition>(
            history,
            FromStart,
            static ctx => ctx.LogPosition,
            retryPipeline);

        var positions = new List<long>();

        await foreach (var e in snapshot.ReadAsync())
            positions.Add(e.Context.LogPosition.ToInt64());

        return positions;
    }

    /// <summary>
    /// Stands in for a store's snapshot whose first read delivers one event and then fails with a transient error, and
    /// whose later reads return no events.
    /// </summary>
    private sealed class FailingHistorySnapshot(long end, long failAfterPosition, List<string> calls) :
        IHistorySnapshot<string>
    {
        private bool _hasFailed;

        public long? End => end;

        public async IAsyncEnumerable<ReadEvent<string, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            calls.Add($"read from {startExclusive} to {endInclusive}");
            await Task.CompletedTask;

            if (_hasFailed)
                yield break;

            var context = ReadEventContext.Create(
                "stream-1",
                "OrderPlaced",
                DateTimeOffset.UnixEpoch,
                StreamPosition.FromInt64(failAfterPosition),
                LogPosition.FromInt64(failAfterPosition));

            yield return ReadEvent.Create("payload", new Dictionary<string, string>(), context);

            _hasFailed = true;
            throw new IOException("The read failed.");
        }
    }
}