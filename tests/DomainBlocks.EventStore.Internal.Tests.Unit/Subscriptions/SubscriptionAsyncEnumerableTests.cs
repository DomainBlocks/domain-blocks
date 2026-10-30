using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

using Event = ReadEvent<string, string, StreamPosition, LogPosition>;

public class SubscriptionAsyncEnumerableTests
{
    private const long FeedMark = 10;
    private const long ReadMark = 20;

    [Test]
    public async Task GetAsyncEnumerator_FromStart_ReadsEndAtFeedMarkAndReplaysAtReadMark()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.Start).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"end at {FeedMark}", $"read at {ReadMark} from -1 to 7"]);
    }

    [Test]
    public async Task GetAsyncEnumerator_FromEnd_AttachesBeforeReadingEndAndReplaysNothing()
    {
        var calls = new List<string>();
        var feed = new TestFeed(calls);
        var reader = new TestCatchUpReader(calls, end: 7);

        await using var enumerator = Subscribe(feed, reader, SubscriptionOrigin.End).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        enumerator.Current.IsCaughtUp.ShouldBeTrue();
        calls.ShouldBe(["attach", $"end at {FeedMark}"]);
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
        calls.ShouldBe(["attach", "detach", $"end at {FeedMark}", $"read at {ReadMark} from 7 to 9"]);
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
        calls.ShouldBe(["attach", "detach", $"end at {FeedMark}", $"read at {ReadMark} from -1 to 2"]);
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
    /// Stands in for a store's catch-up reader, with an empty replay and a read mark of <see cref="ReadMark"/>, and
    /// notes each read.
    /// </summary>
    private sealed class TestCatchUpReader(List<string> calls, long? end) : ICatchUpReader<string, long>
    {
        public long? End { get; set; } = end;

        public ValueTask<(long? End, long Mark)> ReadEndAsync(
            long mark,
            string correlationId,
            CancellationToken cancellationToken)
        {
            calls.Add($"end at {mark}");
            return ValueTask.FromResult((End, ReadMark));
        }

        public async IAsyncEnumerable<Event> ReadAsync(
            long mark,
            long startExclusive,
            long endInclusive,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            calls.Add($"read at {mark} from {startExclusive} to {endInclusive}");
            await Task.CompletedTask;
            yield break;
        }
    }
}