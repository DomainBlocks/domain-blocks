using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class SubscriptionAsyncEnumerableTests
{
    private const long FeedMark = 10;
    private const int TestTimeoutMillis = 5 * 1000;

    [Test]
    public async Task GetAsyncEnumerator_FromStart_ReadsAtFeedMarkAndReplaysUpToEnd()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"catch-up at {FeedMark}", "read from -1 to 7", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEnd_AttachesBeforeReadingOnceAndReplaysNothing()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"catch-up at {FeedMark}", "dispose"]);
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
        calls.ShouldBe(["attach", "detach", $"catch-up at {FeedMark}", "read from 7 to 9", "dispose"]);
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
        calls.ShouldBe(["attach", "detach", $"catch-up at {FeedMark}", "read from -1 to 2", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_AfterPositionPastEndWhenRestarted_ReplaysFromPosition()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);
        var origin = SubscriptionOrigin.After(LogPosition.FromInt64(9));

        await using var enumerator = Subscribe(feed, reader, origin).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        calls.Clear();
        reader.End = 11;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);
        await enumerator.MoveNextAsync();

        enumerator.Current.IsFellBehind.ShouldBeTrue();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", "detach", $"catch-up at {FeedMark}", "read from 9 to 11", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_RestartSignaledDuringReplay_FinishesReplayBeforeRestarting()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 2) { EventPositions = [0, 1, 2] };

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
        calls.ShouldBe(["dispose", "attach", "detach", $"catch-up at {FeedMark}", "read from 2 to 4", "dispose"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_RestartAfterFilteredReplay_ReplaysFromEndOfFirstReplay()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);

        // The filter selects the first event in the sequence and not the two that follow it.
        var reader = new TestCatchUpReader(calls, end: 2) { EventPositions = [0] };

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.Event.ShouldNotBeNull().Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));

        calls.Clear();
        reader.End = 4;
        await feed.Observers[^1].OnResetAsync(CancellationToken.None);
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["dispose", "attach", "detach", $"catch-up at {FeedMark}", "read from 2 to 4", "dispose"]);
    }

    [Test]
    public async Task DisposeAsync_DuringReplay_DisposesCatchUpBeforeDetaching()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 1) { EventPositions = [0, 1] };

        var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        await enumerator.DisposeAsync();

        calls.ShouldBe(["attach", $"catch-up at {FeedMark}", "read from -1 to 1", "dispose", "detach"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_FeedFailsPastLimitBeforeConnecting_ThrowsLastError(CancellationToken ct)
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls) { Connection = new TaskCompletionSource().Task };
        var reader = new TestCatchUpReader(calls, end: 7);
        var options = new SubscriptionOptions { MaxRetryAttempts = 1 };
        var error = new IOException("down");

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start, options).GetAsyncEnumerator(ct);
        var moving = enumerator.MoveNextAsync().AsTask();

        await feed.Observers[^1].OnFailureAsync(Failure(1, hasConnected: false), ct);
        moving.IsCompleted.ShouldBeFalse();

        await feed.Observers[^1].OnFailureAsync(Failure(2, hasConnected: false, error), ct);

        (await Should.ThrowAsync<IOException>(() => moving)).ShouldBeSameAs(error);
        calls.ShouldBe(["attach", "detach"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_FeedConnectsWithinLimit_CatchesUp(CancellationToken ct)
    {
        var calls = new List<string>();
        var connection = new TaskCompletionSource();
        var feed = new TestFeed(calls) { Connection = connection.Task };
        var reader = new TestCatchUpReader(calls, end: 7);
        var options = new SubscriptionOptions { MaxRetryAttempts = 1 };

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End, options).GetAsyncEnumerator(ct);
        var moving = enumerator.MoveNextAsync().AsTask();

        await feed.Observers[^1].OnFailureAsync(Failure(1, hasConnected: false), ct);
        connection.SetResult();

        await moving;
        enumerator.Current.IsCaughtUp.ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_FeedFailsDuringReplay_DoesNotCountUntilLive(CancellationToken ct)
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 1) { EventPositions = [0, 1] };
        var options = new SubscriptionOptions { MaxRetryAttempts = 0 };
        var error = new IOException("lost");

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start, options).GetAsyncEnumerator(ct);
        await enumerator.MoveNextAsync();

        await feed.Observers[^1].OnFailureAsync(Failure(1, hasConnected: true), ct);

        await enumerator.MoveNextAsync();
        enumerator.Current.Event.ShouldNotBeNull().Context.LogPosition.ShouldBe(LogPosition.FromInt64(1));
        await enumerator.MoveNextAsync();
        enumerator.Current.IsCaughtUp.ShouldBeTrue();

        // The subscription depends on the feed from the moment it is told that it caught up.
        await feed.Observers[^1].OnFailureAsync(Failure(2, hasConnected: true, error), ct);

        (await Should.ThrowAsync<IOException>(() => enumerator.MoveNextAsync().AsTask())).ShouldBeSameAs(error);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_FeedFailsPastRetryTimeoutWhileLive_ThrowsLastError(CancellationToken ct)
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);
        var retryTimeout = TimeSpan.FromMinutes(5);
        var options = new SubscriptionOptions { RetryTimeout = retryTimeout };
        var error = new IOException("lost");

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End, options).GetAsyncEnumerator(ct);
        await enumerator.MoveNextAsync();

        await feed.Observers[^1].OnFailureAsync(Failure(1, hasConnected: true), ct);
        await feed.Observers[^1].OnFailureAsync(new FeedFailure(error, 2, retryTimeout, hasConnected: true), ct);

        (await Should.ThrowAsync<IOException>(() => enumerator.MoveNextAsync().AsTask())).ShouldBeSameAs(error);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task GetAsyncEnumerator_Restarted_StartsLimitsAgain(CancellationToken ct)
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);
        var options = new SubscriptionOptions { MaxRetryAttempts = 1 };

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End, options).GetAsyncEnumerator(ct);
        await enumerator.MoveNextAsync();

        await feed.Observers[^1].OnFailureAsync(Failure(1, hasConnected: true), ct);
        await feed.Observers[^1].OnResetAsync(ct);
        await enumerator.MoveNextAsync();
        enumerator.Current.IsFellBehind.ShouldBeTrue();
        await enumerator.MoveNextAsync();
        enumerator.Current.IsCaughtUp.ShouldBeTrue();

        // With one count across restarts, this failure would reach the limit.
        await feed.Observers[^1].OnFailureAsync(Failure(2, hasConnected: true), ct);
        var moving = enumerator.MoveNextAsync().AsTask();
        moving.IsCompleted.ShouldBeFalse();

        await feed.Observers[^1].OnFailureAsync(Failure(3, hasConnected: true), ct);

        await Should.ThrowAsync<IOException>(() => moving);
    }

    private static FeedFailure Failure(int failureCount, bool hasConnected, Exception? error = null) =>
        new(error ?? new IOException("lost"), failureCount, TimeSpan.Zero, hasConnected);

    private static SubscriptionAsyncEnumerable<string, TestSourceEvent, long, LogPosition> Subscribe(
        TestFeed feed,
        TestCatchUpReader reader,
        SubscriptionOrigin<LogPosition> origin,
        SubscriptionOptions? options = null)
    {
        return new SubscriptionAsyncEnumerable<string, TestSourceEvent, long, LogPosition>(
            feed,
            reader,
            EventFilter.All,
            origin,
            options ?? SubscriptionOptions.Default,
            CorrelationId.ReserveGenerated(),
            logger: null);
    }

    /// <summary>
    /// Stands in for a store's feed, with a high-water mark of <see cref="FeedMark"/>, and notes each attach and
    /// detach. It has connected unless a test says otherwise.
    /// </summary>
    private sealed class TestFeed(List<string> calls) : IRefCountedFeed<TestSourceEvent, long>
    {
        public List<IFeedObserver<TestSourceEvent>> Observers { get; } = [];

        public Task Connection { get; init; } = Task.CompletedTask;

        public Task<IFeedAttachment<long>> AttachAsync(
            IFeedObserver<TestSourceEvent> observer,
            string correlationId = "unknown",
            CancellationToken cancellationToken = default)
        {
            calls.Add("attach");
            Observers.Add(observer);

            return Task.FromResult<IFeedAttachment<long>>(new TestAttachment(calls, Connection));
        }
    }

    private sealed class TestAttachment(List<string> calls, Task connection) : IFeedAttachment<long>
    {
        public async Task<long> WaitForConnectionAsync(CancellationToken cancellationToken)
        {
            await connection.WaitAsync(cancellationToken);
            return FeedMark;
        }

        public ValueTask DisposeAsync()
        {
            calls.Add("detach");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for a store's reader, and notes each catch-up that it reads.
    /// </summary>
    private sealed class TestCatchUpReader(List<string> calls, long? end) :
        CatchUpReader<string, LogPosition, long>(
            static _ => false,
            SubscriptionOptions.Default,
            "sub-1",
            logger: null)
    {
        public long? End { get; set; } = end;

        public IReadOnlyList<long> EventPositions { get; init; } = [];

        protected override ValueTask<CatchUp<string, LogPosition>> ReadCoreAsync(
            SubscriptionOrigin<LogPosition> origin,
            long highWaterMark,
            CancellationToken cancellationToken)
        {
            calls.Add($"catch-up at {highWaterMark}");

            return ValueTask.FromResult<CatchUp<string, LogPosition>>(
                new TestCatchUp(origin, End, ResiliencePipeline, calls, EventPositions));
        }
    }
}