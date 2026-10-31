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
        var reader = new TestSnapshotReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"snapshot at {FeedMark}", "read from -1 to 7", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEnd_AttachesBeforeReadingSnapshotOnceAndReplaysNothing()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestSnapshotReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"snapshot at {FeedMark}", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEndWhenRestarted_ReplaysFromPinnedEnd()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestSnapshotReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        calls.Clear();
        reader.End = 9;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);
        await enumerator.MoveNextAsync();

        enumerator.Current.IsFellBehind.ShouldBeTrue();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", "detach", $"snapshot at {FeedMark}", "read from 7 to 9", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEndOfEmptySequenceWhenRestarted_ReplaysFromStart()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestSnapshotReader(calls, end: null);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        calls.Clear();
        reader.End = 2;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);
        await enumerator.MoveNextAsync();

        enumerator.Current.IsFellBehind.ShouldBeTrue();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", "detach", $"snapshot at {FeedMark}", "read from -1 to 2", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_RestartSignaledDuringReplay_FinishesReplayBeforeRestarting()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestSnapshotReader(calls, end: 2) { EventPositions = [0, 1, 2] };

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.Event.ShouldNotBeNull().Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));

        calls.Clear();
        reader.End = 4;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);

        await enumerator.MoveNextAsync();
        enumerator.Current.Event.ShouldNotBeNull().Context.LogPosition.ShouldBe(LogPosition.FromInt64(1));
        await enumerator.MoveNextAsync();
        enumerator.Current.Event.ShouldNotBeNull().Context.LogPosition.ShouldBe(LogPosition.FromInt64(2));
        await enumerator.MoveNextAsync();

        // The subscriber had not been told that it caught up, so it is not told that it fell behind.
        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["dispose", "attach", "detach", $"snapshot at {FeedMark}", "read from 2 to 4", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_RestartAfterFilteredReplay_ReplaysFromEndOfFirstReplay()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);

        // The filter selects the first event in the sequence and not the two that follow it.
        var reader = new TestSnapshotReader(calls, end: 2) { EventPositions = [0] };

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.Event.ShouldNotBeNull().Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));

        calls.Clear();
        reader.End = 4;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["dispose", "attach", "detach", $"snapshot at {FeedMark}", "read from 2 to 4", "dispose"]);
    }

    [Test]
    public async Task DisposeAsync_DuringReplay_DisposesSnapshotBeforeDetaching()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestSnapshotReader(calls, end: 1) { EventPositions = [0, 1] };

        var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        await enumerator.DisposeAsync();

        calls.ShouldBe(["attach", $"snapshot at {FeedMark}", "read from -1 to 1", "dispose", "detach"]);
    }

    private static SubscriptionAsyncEnumerable<string, TestSourceEvent, long, LogPosition> Subscribe(
        TestFeed feed,
        TestSnapshotReader reader,
        SubscriptionOrigin<LogPosition> origin)
    {
        return new SubscriptionAsyncEnumerable<string, TestSourceEvent, long, LogPosition>(
            feed,
            reader,
            EventFilter.All,
            origin,
            SubscriptionOptions.Default.QueueCapacity,
            CorrelationId.ReserveGenerated(),
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
    /// Stands in for a store's reader, and notes each snapshot that it reads.
    /// </summary>
    private sealed class TestSnapshotReader(List<string> calls, long? end) :
        CatchUpSnapshotReader<string, LogPosition, long>(
            static _ => false,
            SubscriptionOptions.Default,
            "sub-1",
            logger: null)
    {
        public long? End { get; set; } = end;

        public IReadOnlyList<long> EventPositions { get; init; } = [];

        protected override ValueTask<CatchUpSnapshot<string, LogPosition>> ReadSnapshotCoreAsync(
            SubscriptionOrigin<LogPosition> origin,
            long highWaterMark,
            CancellationToken cancellationToken)
        {
            calls.Add($"snapshot at {highWaterMark}");

            return ValueTask.FromResult<CatchUpSnapshot<string, LogPosition>>(
                new TestCatchUpSnapshot(origin, End, RetryPolicy, calls, EventPositions));
        }
    }
}