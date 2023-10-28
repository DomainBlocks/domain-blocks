using DomainBlocks.Core.Subscriptions;
using DomainBlocks.Core.Tests.Subscriptions.Testing;
using Moq;
using NUnit.Framework;

namespace DomainBlocks.Core.Tests.Subscriptions;

[TestFixture]
[Timeout(1000)]
public class EventStreamSubscriptionBaseTests
{
    private Mock<IEventStreamConsumer<string, int>> _mockSubscriber = null!;
    private TestableEventStreamSubscription _subscription = null!;

    [SetUp]
    public async Task SetUp()
    {
        _mockSubscriber = new Mock<IEventStreamConsumer<string, int>>();
        _subscription = new TestableEventStreamSubscription(new[] { _mockSubscriber.Object });
        await _subscription.StartAsync();
    }

    [Test]
    public async Task CatchingUpNotificationIsHandled()
    {
        var signal = new TaskCompletionSource();

        _mockSubscriber
            .Setup(x => x.OnCatchingUp(It.IsAny<CancellationToken>()))
            .Callback(() => signal.SetResult());

        await _subscription.NotifyCatchingUp();
        await signal.Task;

        _mockSubscriber.Verify(x => x.OnCatchingUp(It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task EventNotificationIsHandled()
    {
        var signal = new TaskCompletionSource();

        _mockSubscriber
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnEventResult.Processed)
            .Callback(() => signal.SetResult());

        await _subscription.NotifyEvent("event1", 0);
        await signal.Task;

        _mockSubscriber.Verify(x => x.OnEvent("event1", 0, It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task LiveNotificationIsHandled()
    {
        var signal = new TaskCompletionSource();

        _mockSubscriber
            .Setup(x => x.OnLive(It.IsAny<CancellationToken>()))
            .Callback(() => signal.SetResult());

        await _subscription.NotifyLive();
        await signal.Task;

        _mockSubscriber.Verify(x => x.OnLive(It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task AbortedEventErrorIsHandled()
    {
        var signal = new TaskCompletionSource();
        var exception = new Exception("Something bad happened");

        _mockSubscriber
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception)
            .Callback(() => signal.SetResult());

        _mockSubscriber
            .Setup(x => x.OnEventError(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventErrorResolution.Abort);

        await _subscription.NotifyEvent("event1", 0);

        Assert.ThrowsAsync<Exception>(() => _subscription.WaitForCompletedAsync(), exception.Message);
    }

    [Test]
    public async Task SkippedEventErrorIsHandled()
    {
        var signal = new TaskCompletionSource();
        var exception = new Exception("Error handling event");
        var onEventResults = new Queue<Func<string, int, CancellationToken, Task<OnEventResult>>>();

        // Set first invocation of OnEvent to throw.
        onEventResults.Enqueue((_, _, _) => throw exception);

        // Set second invocation of OnEvent to succeed.
        onEventResults.Enqueue((_, _, _) =>
        {
            signal.SetResult();
            return Task.FromResult(OnEventResult.Processed);
        });

        _mockSubscriber
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, CancellationToken>((e, p, ct) => onEventResults.Dequeue()(e, p, ct));

        _mockSubscriber
            .Setup(x => x.OnEventError(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventErrorResolution.Skip);

        await _subscription.NotifyEvent("event1", 0);

        // Check we can handle a second event after skipping the error.
        await _subscription.NotifyEvent("event2", 1);
        await signal.Task;
        _mockSubscriber.Verify(x => x.OnEvent("event2", 1, It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task RetriedEventErrorIsHandled()
    {
        var signal = new TaskCompletionSource();
        var exception = new Exception("Error handling event");
        var onEventResults = new Queue<Func<string, int, CancellationToken, Task<OnEventResult>>>();

        // Set first invocation of OnEvent to throw.
        onEventResults.Enqueue((_, _, _) => throw exception);

        // Set second invocation of OnEvent to succeed.
        onEventResults.Enqueue((_, _, _) =>
        {
            signal.SetResult();
            return Task.FromResult(OnEventResult.Processed);
        });

        _mockSubscriber
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, CancellationToken>((e, p, ct) => onEventResults.Dequeue()(e, p, ct));

        _mockSubscriber
            .Setup(x => x.OnEventError(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventErrorResolution.Retry);

        await _subscription.NotifyEvent("event1", 0);
        await signal.Task;

        _mockSubscriber.Verify(x => x.OnEvent("event1", 0, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public void ExceptionWhileCatchingUpDoesNotBlockStartAsync()
    {
        // We need to emit at least two events to test this behaviour. This is because when the first call to OnEvent
        // throws, the event loop terminates without releasing the empty count semaphore. This means that awaiting
        // StartAsync would block indefinitely while the Subscribe method waits to push the second event - which it
        // can't due to the unreleased semaphore. The fix to stop StartAsync from waiting indefinitely in this situation
        // is to also await for the event loop completion signal using Task.WhenAny. This way, if the event loop does
        // terminate early, we can also exit from StartAsync and throw any exception that occured.
        _subscription.SetCatchupEvents("event1", "event2");

        var exception = new Exception("Something bad happened");

        _mockSubscriber
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        Assert.ThrowsAsync<Exception>(() => _subscription.StartAsync(), exception.Message);
    }

    [Test]
    public async Task SubscriptionSubscribesFromMinimumStartPositionOfSubscribers()
    {
        const int startPosition1 = 1;
        const int startPosition2 = 2;

        var mockSubscriber1 = new Mock<IEventStreamConsumer<string, int>>();
        var mockSubscriber2 = new Mock<IEventStreamConsumer<string, int>>();

        mockSubscriber1.Setup(x => x.OnStarting(It.IsAny<CancellationToken>())).ReturnsAsync(startPosition1);
        mockSubscriber2.Setup(x => x.OnStarting(It.IsAny<CancellationToken>())).ReturnsAsync(startPosition2);

        var subscription =
            new TestableEventStreamSubscription(new[] { mockSubscriber1.Object, mockSubscriber2.Object });

        await subscription.StartAsync();

        Assert.That(subscription.StartPositionSubscribedFrom, Is.EqualTo(startPosition1));
    }

    [Test]
    public void EventBeforeSubscriberStartPositionIsIgnored()
    {
        // TODO (DS, CF): fix everything
    }
}