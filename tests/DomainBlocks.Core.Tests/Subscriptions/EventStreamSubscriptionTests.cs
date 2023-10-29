using DomainBlocks.Core.Subscriptions;
using Moq;
using NUnit.Framework;

namespace DomainBlocks.Core.Tests.Subscriptions;

[TestFixture]
[Timeout(1000)]
public class EventStreamSubscriptionTests
{
    private TestableEventStreamSubscriber<string, int> _subscriber = null!;
    private Mock<IEventStreamConsumer<string, int>> _mockConsumer = null!;
    private EventStreamSubscription<string, int> _subscription = null!;

    [SetUp]
    public void SetUp()
    {
        _subscriber = new TestableEventStreamSubscriber<string, int>();

        _mockConsumer = new Mock<IEventStreamConsumer<string, int>>();
        _mockConsumer.Setup(x => x.CatchUpCheckpointFrequency).Returns(CheckpointFrequency.Default);
        _mockConsumer.Setup(x => x.LiveCheckpointFrequency).Returns(CheckpointFrequency.Default);

        _subscription = new EventStreamSubscription<string, int>(_subscriber, new[] { _mockConsumer.Object });
    }

    [Test]
    public async Task CatchingUpNotificationIsHandled()
    {
        var signal = new TaskCompletionSource();

        _mockConsumer
            .Setup(x => x.OnCatchingUp(It.IsAny<CancellationToken>()))
            .Callback(() => signal.SetResult());

        var startTask = _subscription.StartAsync();
        await _subscriber.InvokeOnCatchingUp();
        _subscriber.CompleteSubscribing();
        await startTask;
        await signal.Task;

        _mockConsumer.Verify(x => x.OnCatchingUp(It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task EventNotificationIsHandled()
    {
        var signal = new TaskCompletionSource();

        _mockConsumer
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnEventResult.Processed)
            .Callback(() => signal.SetResult());

        var startTask = _subscription.StartAsync();
        await _subscriber.InvokeOnEvent("event1", 0);
        _subscriber.CompleteSubscribing();
        await startTask;
        await signal.Task;

        _mockConsumer.Verify(x => x.OnEvent("event1", 0, It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task LiveNotificationIsHandled()
    {
        var signal = new TaskCompletionSource();

        _mockConsumer
            .Setup(x => x.OnLive(It.IsAny<CancellationToken>()))
            .Callback(() => signal.SetResult());

        var startTask = _subscription.StartAsync();
        await _subscriber.InvokeOnLive();
        _subscriber.CompleteSubscribing();
        await startTask;
        await signal.Task;

        _mockConsumer.Verify(x => x.OnLive(It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task AbortedEventErrorIsHandled()
    {
        var exception = new Exception("Something bad happened");

        _mockConsumer
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        _mockConsumer
            .Setup(x => x.OnEventError(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventErrorResolution.Abort);

        var startTask = _subscription.StartAsync();
        await _subscriber.InvokeOnEvent("event1", 0);

        Assert.ThrowsAsync<Exception>(() => startTask, exception.Message);
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

        _mockConsumer
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, CancellationToken>((e, p, ct) => onEventResults.Dequeue()(e, p, ct));

        _mockConsumer
            .Setup(x => x.OnEventError(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventErrorResolution.Skip);

        var startTask = _subscription.StartAsync();
        await _subscriber.InvokeOnEvent("event1", 0);

        // Check we can handle a second event after skipping the error.
        await _subscriber.InvokeOnEvent("event2", 1);

        _subscriber.CompleteSubscribing();
        await startTask;
        await signal.Task;

        _mockConsumer.Verify(x => x.OnEvent("event2", 1, It.IsAny<CancellationToken>()));
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

        _mockConsumer
            .Setup(x => x.OnEvent(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, CancellationToken>((e, p, ct) => onEventResults.Dequeue()(e, p, ct));

        _mockConsumer
            .Setup(x => x.OnEventError(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventErrorResolution.Retry);

        var startTask = _subscription.StartAsync();
        await _subscriber.InvokeOnEvent("event1", 0);
        _subscriber.CompleteSubscribing();
        await startTask;
        await signal.Task;

        _mockConsumer.Verify(x => x.OnEvent("event1", 0, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task SubscriptionSubscribesFromMinimumStartPositionOfSubscribers()
    {
        const int startPosition1 = 1;
        const int startPosition2 = 2;

        var mockConsumer1 = new Mock<IEventStreamConsumer<string, int>>();
        var mockConsumer2 = new Mock<IEventStreamConsumer<string, int>>();

        mockConsumer1.Setup(x => x.OnStarting(It.IsAny<CancellationToken>())).ReturnsAsync(startPosition1);
        mockConsumer2.Setup(x => x.OnStarting(It.IsAny<CancellationToken>())).ReturnsAsync(startPosition2);

        var subscription = new EventStreamSubscription<string, int>(
            _subscriber, new[] { mockConsumer1.Object, mockConsumer2.Object });

        var startTask = subscription.StartAsync();
        _subscriber.CompleteSubscribing();
        await startTask;

        Assert.That(_subscriber.StartPosition, Is.EqualTo(startPosition1));
    }

    [Test]
    public void EventBeforeSubscriberStartPositionIsIgnored()
    {
        // TODO (DS, CF): fix everything
    }
}