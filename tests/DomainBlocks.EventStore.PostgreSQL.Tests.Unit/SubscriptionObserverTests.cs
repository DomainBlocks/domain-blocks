using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

using Observer = SubscriptionAsyncEnumerable<string, LogPosition>.Observer;
using RestartReason = SubscriptionAsyncEnumerable<string, LogPosition>.RestartReason;

public class SubscriptionObserverTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private CountingDecoder _decoder = null!;
    private EventLogRow<string> _row = null!;

    [SetUp]
    public void SetUp()
    {
        _decoder = new CountingDecoder();
        _row = new EventLogRow<string>(_decoder);
    }

    [Test]
    public async Task OnNextAsync_SelectedRow_QueuesItsEvent()
    {
        using var observer = new Observer(queueCapacity: 2, EventFilter.StreamIds("order-1"));

        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-2");
        await OfferAsync(observer, 2, "order-1");

        observer.Reader.TryRead(out var first).ShouldBeTrue();
        observer.Reader.TryRead(out var second).ShouldBeTrue();
        observer.Reader.TryRead(out _).ShouldBeFalse();
        first.Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));
        second.Context.LogPosition.ShouldBe(LogPosition.FromInt64(2));
        _decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public async Task OnNextAsync_WhenQueueIsFull_SignalsRestart()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);

        await OfferAsync(observer, 0, "order-1");
        observer.RestartReason.ShouldBe(RestartReason.None);

        await OfferAsync(observer, 1, "order-1");

        observer.RestartReason.ShouldBe(RestartReason.QueueOverflow);
    }

    [Test]
    public async Task OnNextAsync_WhenQueueIsFull_EndsReaderOnceQueuedEventIsRead()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");

        await OfferAsync(observer, 1, "order-1");

        (await observer.Reader.WaitToReadAsync()).ShouldBeTrue();
        observer.Reader.TryRead(out var queued).ShouldBeTrue();
        queued.Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));
        (await observer.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task OnResetAsync_WhenAlreadyStoppedByQueueOverflow_KeepsFirstReason()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");

        await observer.OnResetAsync(CancellationToken.None);

        observer.RestartReason.ShouldBe(RestartReason.QueueOverflow);
    }

    [Test]
    public async Task OnNextAsync_AfterQueueOverflowed_DoesNotDecodeFurtherRows()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");
        var decodeCountAtOverflow = _decoder.DecodeCount;

        await OfferAsync(observer, 2, "order-1");
        await OfferAsync(observer, 3, "order-1");

        _decoder.DecodeCount.ShouldBe(decodeCountAtOverflow);
    }

    [Test]
    public async Task OnNextAsync_AfterFeedReset_DoesNotDecodeFurtherRows()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await observer.OnResetAsync(CancellationToken.None);

        await OfferAsync(observer, 1, "order-1");

        _decoder.DecodeCount.ShouldBe(1);
        observer.RestartReason.ShouldBe(RestartReason.FeedReset);
    }

    [Test]
    public async Task OnNextAsync_AfterRowCouldNotBeDecoded_FailsReaderAndDoesNotDecodeFurtherRows()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        _decoder.Failure = new InvalidOperationException("Cannot decode.");
        await OfferAsync(observer, 0, "order-1");

        _decoder.Failure = null;
        await OfferAsync(observer, 1, "order-1");

        _decoder.DecodeCount.ShouldBe(1);
        observer.RestartReason.ShouldBe(RestartReason.None);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observer.Reader.WaitToReadAsync());

        exception.Message.ShouldBe("Cannot decode.");
    }

    private ValueTask OfferAsync(Observer observer, long position, string streamId)
    {
        _row.Set(position, streamId, position, "OrderPlaced", PostgresEventData.FromJson("{}"), null, CreatedAt);

        return observer.OnNextAsync(_row, CancellationToken.None);
    }
}