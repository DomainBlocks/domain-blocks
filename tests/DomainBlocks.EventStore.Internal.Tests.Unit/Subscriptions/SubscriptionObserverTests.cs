using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

using Observer = SubscriptionObserver<string, TestSourceEvent>;

public class SubscriptionObserverTests
{
    private TestSourceEvent _sourceEvent = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceEvent = new TestSourceEvent();
    }

    [Test]
    public async Task OnNextAsync_SelectedItem_QueuesItsEvent()
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
        _sourceEvent.DecodeCount.ShouldBe(2);
    }

    [Test]
    public async Task OnNextAsync_WhenQueueIsFull_SignalsRestart()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);

        await OfferAsync(observer, 0, "order-1");
        observer.StopReason.ShouldBe(SubscriptionStopReason.None);

        await OfferAsync(observer, 1, "order-1");

        observer.StopReason.ShouldBe(SubscriptionStopReason.QueueOverflow);
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
    public async Task OnNextAsync_WhenQueueIsFull_LogsOverflowAsWarningOnceWhenItHappens()
    {
        var logger = new FakeLogger();
        using var observer = new Observer(queueCapacity: 1, EventFilter.All, logger, "sub-1");

        await OfferAsync(observer, 0, "order-1");
        logger.Collector.GetSnapshot().ShouldBeEmpty();

        await OfferAsync(observer, 1, "order-1");
        await OfferAsync(observer, 2, "order-1");

        logger.Collector.GetSnapshot().Select(record => (record.Level, record.Message)).ShouldBe(
            [(LogLevel.Warning, "[sub: sub-1] queue overflowed (capacity 1); restart pending")]);
    }

    [Test]
    public async Task OnNextAsync_AfterQueueOverflowed_DoesNotDecodeFurtherItems()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");
        var decodeCountAtOverflow = _sourceEvent.DecodeCount;

        await OfferAsync(observer, 2, "order-1");
        await OfferAsync(observer, 3, "order-1");

        _sourceEvent.DecodeCount.ShouldBe(decodeCountAtOverflow);
    }

    [Test]
    public async Task OnNextAsync_AfterFeedReset_DoesNotDecodeFurtherItems()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await observer.OnResetAsync(CancellationToken.None);

        await OfferAsync(observer, 1, "order-1");

        _sourceEvent.DecodeCount.ShouldBe(1);
        observer.StopReason.ShouldBe(SubscriptionStopReason.FeedReset);
    }

    [Test]
    public async Task OnNextAsync_AfterItemCouldNotBeDecoded_FailsReaderAndDoesNotDecodeFurtherItems()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        _sourceEvent.DecodeFailure = new InvalidOperationException("Cannot decode.");
        await OfferAsync(observer, 0, "order-1");

        _sourceEvent.DecodeFailure = null;
        await OfferAsync(observer, 1, "order-1");

        _sourceEvent.DecodeCount.ShouldBe(1);
        observer.StopReason.ShouldBe(SubscriptionStopReason.None);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observer.Reader.WaitToReadAsync());

        exception.Message.ShouldBe("Cannot decode.");
    }

    [Test]
    public async Task OnResetAsync_WhenNotYetStopped_SignalsRestart()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");

        await observer.OnResetAsync(CancellationToken.None);

        observer.StopReason.ShouldBe(SubscriptionStopReason.FeedReset);
        observer.Reader.TryRead(out _).ShouldBeTrue();
        (await observer.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task OnResetAsync_WhenNotYetStopped_LogsFeedResetAsWarningOnce()
    {
        var logger = new FakeLogger();
        using var observer = new Observer(queueCapacity: 1, EventFilter.All, logger, "sub-1");

        await observer.OnResetAsync(CancellationToken.None);
        await observer.OnResetAsync(CancellationToken.None);

        logger.Collector.GetSnapshot().Select(record => (record.Level, record.Message)).ShouldBe(
            [(LogLevel.Warning, "[sub: sub-1] feed was reset; restart pending")]);
    }

    [Test]
    public async Task OnResetAsync_WhenAlreadyStoppedByQueueOverflow_KeepsFirstReason()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");

        await observer.OnResetAsync(CancellationToken.None);

        observer.StopReason.ShouldBe(SubscriptionStopReason.QueueOverflow);
    }

    [Test]
    public async Task OnErrorAsync_FeedError_FailsReader()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        var error = new InvalidOperationException("Feed failed.");

        await observer.OnErrorAsync(error, CancellationToken.None);

        observer.StopReason.ShouldBe(SubscriptionStopReason.None);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observer.Reader.WaitToReadAsync());

        exception.ShouldBeSameAs(error);
    }

    [Test]
    public async Task Dispose_WhenAlreadyStoppedByQueueOverflow_KeepsFirstReason()
    {
        var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");

        observer.Dispose();

        observer.StopReason.ShouldBe(SubscriptionStopReason.QueueOverflow);
    }

    [Test]
    public async Task Dispose_WhenNotYetStopped_EndsReaderOnceQueuedEventIsRead()
    {
        var observer = new Observer(queueCapacity: 10, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");

        observer.Dispose();

        observer.StopReason.ShouldBe(SubscriptionStopReason.Disposed);
        observer.Reader.TryRead(out _).ShouldBeTrue();
        (await observer.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    private ValueTask OfferAsync(Observer observer, long position, string streamId)
    {
        _sourceEvent.Set(position, streamId);

        return observer.OnNextAsync(_sourceEvent, CancellationToken.None);
    }
}