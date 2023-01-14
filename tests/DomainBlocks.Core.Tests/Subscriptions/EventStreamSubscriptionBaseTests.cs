using DomainBlocks.Core.Subscriptions;
using DomainBlocks.Core.Tests.Subscriptions.Testing;
using Moq;
using NUnit.Framework;

namespace DomainBlocks.Core.Tests.Subscriptions;

[TestFixture]
[Timeout(1000)]
public class EventStreamSubscriptionBaseTests
{
    private Mock<IEventStreamSubscriber<string, int>> _mockSubscriber = null!;
    private TestableEventStreamSubscription _subscription = null!;

    [SetUp]
    public async Task SetUp()
    {
        _mockSubscriber = new Mock<IEventStreamSubscriber<string, int>>();
        _subscription = new TestableEventStreamSubscription(_mockSubscriber.Object);
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
}