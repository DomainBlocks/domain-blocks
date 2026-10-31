using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;
using NUnit.Framework;
using Polly;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class CatchUpTests
{
    private static readonly SubscriptionOrigin<LogPosition> FromStart = SubscriptionOrigin.Start;
    private static readonly SubscriptionOrigin<LogPosition> FromEnd = SubscriptionOrigin.End;

    [Test]
    public async Task ReadAll_FromStart_ReadsUpToEnd()
    {
        (await ReadAsync(FromStart, end: 7)).ShouldBe(["read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAll_FromEnd_ReadsNothing()
    {
        (await ReadAsync(FromEnd, end: 7)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAll_AfterPositionBeforeEnd_ReadsFromPositionToEnd()
    {
        (await ReadAsync(After(3), end: 7)).ShouldBe(["read from 3 to 7"]);
    }

    [TestCase(7)]
    [TestCase(9)]
    public async Task ReadAll_AfterPositionAtOrPastEnd_ReadsNothing(long position)
    {
        (await ReadAsync(After(position), end: 7)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAll_EmptySequence_ReadsNothing()
    {
        (await ReadAsync(FromStart, end: null)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAll_ReadTwice_ReadsSameEventsEachTime()
    {
        var calls = new List<string>();
        var catchUp = new TestCatchUp(FromStart, 7, ResiliencePipeline.Empty, calls, [1, 7]);

        (await ReadPositionsAsync(catchUp)).ShouldBe([1, 7]);
        (await ReadPositionsAsync(catchUp)).ShouldBe([1, 7]);

        calls.ShouldBe(["read from -1 to 7", "read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAll_TransientErrorBeforeEnd_ReadsAgainAfterLastEvent()
    {
        var calls = new List<string>();
        var catchUp = new FailingCatchUp(end: 7, failAfterPosition: 3, CreateResiliencePipeline(), calls);

        (await ReadPositionsAsync(catchUp)).ShouldBe([3]);

        calls.ShouldBe(["read from -1 to 7", "read from 3 to 7"]);
    }

    [Test]
    public async Task ReadAll_TransientErrorAtEnd_DoesNotReadAgain()
    {
        var calls = new List<string>();
        var catchUp = new FailingCatchUp(end: 7, failAfterPosition: 7, CreateResiliencePipeline(), calls);

        (await ReadPositionsAsync(catchUp)).ShouldBe([7]);

        calls.ShouldBe(["read from -1 to 7"]);
    }

    [Test]
    public async Task ReadAll_TransientErrorWithoutRetries_Throws()
    {
        var catchUp = new FailingCatchUp(end: 7, failAfterPosition: 3, ResiliencePipeline.Empty, []);

        await Should.ThrowAsync<IOException>(() => ReadPositionsAsync(catchUp));
    }

    [Test]
    public void EndPosition_FromStart_IsEnd()
    {
        CreateCatchUp(FromStart, end: 7).EndPosition.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void EndPosition_FromEnd_IsEnd()
    {
        CreateCatchUp(FromEnd, end: 7).EndPosition.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void EndPosition_AfterPositionBeforeEnd_IsEnd()
    {
        CreateCatchUp(After(3), end: 7).EndPosition.ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void EndPosition_AfterPositionPastEnd_IsPosition()
    {
        CreateCatchUp(After(9), end: 7).EndPosition.ShouldBe(LogPosition.FromInt64(9));
    }

    [Test]
    public void EndPosition_EmptySequenceFromStartOrEnd_IsNull()
    {
        CreateCatchUp(FromStart, end: null).EndPosition.ShouldBeNull();
        CreateCatchUp(FromEnd, end: null).EndPosition.ShouldBeNull();
    }

    [Test]
    public void EndPosition_EmptySequenceAfterPosition_IsPosition()
    {
        CreateCatchUp(After(3), end: null).EndPosition.ShouldBe(LogPosition.FromInt64(3));
    }

    private static SubscriptionOrigin<LogPosition> After(long position) =>
        SubscriptionOrigin.After(LogPosition.FromInt64(position));

    private static TestCatchUp CreateCatchUp(
        SubscriptionOrigin<LogPosition> origin,
        long? end,
        List<string>? calls = null)
    {
        return new TestCatchUp(origin, end, ResiliencePipeline.Empty, calls ?? []);
    }

    // Retries at once.
    private static ResiliencePipeline CreateResiliencePipeline()
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

        await foreach (var _ in CreateCatchUp(origin, end, calls).ReadAll())
        {
        }

        return calls;
    }

    private static async Task<List<long>> ReadPositionsAsync(CatchUp<string, LogPosition> catchUp)
    {
        var positions = new List<long>();

        await foreach (var e in catchUp.ReadAll())
            positions.Add(e.Context.LogPosition.ToInt64());

        return positions;
    }

    /// <summary>
    /// Stands in for a store's catch-up whose first read delivers one event and then fails with a transient error, and
    /// whose later reads return no events.
    /// </summary>
    private sealed class FailingCatchUp(
        long end,
        long failAfterPosition,
        ResiliencePipeline resiliencePipeline,
        List<string> calls) :
        CatchUp<string, LogPosition>(FromStart, end, resiliencePipeline)
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

            yield return TestCatchUp.CreateEvent(failAfterPosition);

            _hasFailed = true;
            throw new IOException("The read failed.");
        }
    }
}