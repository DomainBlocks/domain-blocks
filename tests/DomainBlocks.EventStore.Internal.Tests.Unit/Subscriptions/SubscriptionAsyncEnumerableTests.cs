using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class SubscriptionAsyncEnumerableTests
{
    private const long FeedMark = 10;

    [Test]
    public async Task GetAsyncEnumerator_FromStart_ReadsSnapshotAtFeedMarkAndReplaysIt()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"snapshot at {FeedMark}", "read from -1 to 7"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEnd_AttachesBeforeReadingSnapshotOnceAndReplaysNothing()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"snapshot at {FeedMark}"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEndWhenRestarted_ReplaysFromPinnedEnd()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        calls.Clear();
        reader.End = 9;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);
        await enumerator.MoveNextAsync();

        enumerator.Current.IsFellBehind.ShouldBeTrue();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", "detach", $"snapshot at {FeedMark}", "read from 7 to 9"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEndOfEmptySequenceWhenRestarted_ReplaysFromStart()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: null);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        calls.Clear();
        reader.End = 2;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);
        await enumerator.MoveNextAsync();

        enumerator.Current.IsFellBehind.ShouldBeTrue();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", "detach", $"snapshot at {FeedMark}", "read from -1 to 2"]);
    }

    private static SubscriptionAsyncEnumerable<string, TestSourceEvent, long, LogPosition> Subscribe(
        TestFeed feed,
        TestCatchUpReader reader,
        SubscriptionOrigin<LogPosition> origin)
    {
        return new SubscriptionAsyncEnumerable<string, TestSourceEvent, long, LogPosition>(
            feed,
            reader,
            EventFilter.All,
            static ctx => ctx.LogPosition,
            origin,
            options: null,
            static _ => false,
            logger: null);
    }

    /// <summary>
    /// Stands in for a store's feed, with a high-water mark of <see cref="FeedMark"/>, and notes each attach and
    /// detach.
    /// </summary>
    private sealed class TestFeed(List<string> calls) : IRefCountedFeed<TestSourceEvent, long>
    {
        public List<IFeedObserver<TestSourceEvent>> Observers { get; } = [];

        public Task<IFeedAttachment<long>> AttachAsync(
            IFeedObserver<TestSourceEvent> observer,
            string correlationId = "unknown",
            CancellationToken cancellationToken = default)
        {
            calls.Add("attach");
            Observers.Add(observer);

            return Task.FromResult<IFeedAttachment<long>>(new TestAttachment(calls));
        }
    }

    private sealed class TestAttachment(List<string> calls) : IFeedAttachment<long>
    {
        public long HighWaterMark => FeedMark;

        public ValueTask DisposeAsync()
        {
            calls.Add("detach");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for a store's catch-up reader, and notes each snapshot that it reads.
    /// </summary>
    private sealed class TestCatchUpReader(List<string> calls, long? end) : ICatchUpReader<string, long, LogPosition>
    {
        public long? End { get; set; } = end;

        public ValueTask<CatchUpSnapshot<string, LogPosition>> ReadSnapshotAsync(
            long highWaterMark,
            SubscriptionOrigin<LogPosition> origin,
            string correlationId,
            CancellationToken cancellationToken)
        {
            calls.Add($"snapshot at {highWaterMark}");

            return ValueTask.FromResult<CatchUpSnapshot<string, LogPosition>>(
                new TestCatchUpSnapshot(origin, End, calls));
        }
    }
}