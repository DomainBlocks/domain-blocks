using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Subscriptions;
using Moq;

namespace DomainBlocks.V1.Tests.Subscriptions;

[TestFixture]
public class EventStreamConsumerSessionTests
{
    private Mock<IEventStreamConsumer> _mockConsumer = null!;
    private EventStreamConsumerSession _session = null!;

    [SetUp]
    public void SetUp()
    {
        _mockConsumer = new Mock<IEventStreamConsumer>();
        _session = new EventStreamConsumerSession(_mockConsumer.Object);
    }

    [Test]
    public async Task InitializeAsync_InvokesConsumerOnInitializing()
    {
        await _session.InitializeAsync(CancellationToken.None);
        _mockConsumer.Verify(x => x.OnInitializingAsync(CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task InitializeAsync_WhenCalledMoreThanOnce_ThrowsInvalidOperationException()
    {
        await _session.InitializeAsync(CancellationToken.None);

        await Assert.ThatAsync(
            () => _session.InitializeAsync(CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task StartAsync_ReturnsPositionFromConsumerOnRestoreAsync()
    {
        var position = new SubscriptionPosition(42);
        _mockConsumer.Setup(x => x.OnRestoreAsync(CancellationToken.None)).ReturnsAsync(new SubscriptionPosition(42));

        await _session.InitializeAsync();
        var result = await _session.StartAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(position));
    }

    [Test]
    public async Task StartAsync_StartsReadingMessages()
    {
        await _session.InitializeAsync();
        await _session.StartAsync();
        await _session.FlushAsync();
    }

    [Test]
    public async Task StartAsync_AfterEventHandlerError_ResumesFromFaultedMessage()
    {
        var consumer = new RandomlyFailingConsumer();
        var session = new EventStreamConsumerSession(consumer);

        await session.InitializeAsync();
        await session.StartAsync();

        var contexts = Enumerable
            .Range(0, 1000)
            .Select(i =>
            {
                var position = new SubscriptionPosition((ulong)i);
                return new EventHandlerContext(new TestEvent(), position, CancellationToken.None);
            })
            .ToArray();

        foreach (var context in contexts)
        {
            await session.NotifyEventReceivedAsync(context, CancellationToken.None);
        }

        while (consumer.HandledEvents.Count < contexts.Length)
        {
            await session.WaitForCompletedAsync();

            Assert.That(session.Error, Is.Not.Null);

            await session.StartAsync();
        }

        Assert.That(consumer.HandledEvents, Has.Count.EqualTo(contexts.Length));
        Assert.That(consumer.ErrorCount, Is.GreaterThan(1));
    }

    private class RandomlyFailingConsumer : IEventStreamConsumer, IEventHandler<TestEvent>
    {
        private readonly Random _random = new();
        private readonly List<TestEvent> _handledEvents = new();

        public Task OnEventAsync(EventHandlerContext<TestEvent> context)
        {
            var isError = _random.Next(2) == 1;
            if (isError)
            {
                ErrorCount++;
                throw new Exception("Random error.");
            }

            _handledEvents.Add(context.Event);
            return Task.CompletedTask;
        }

        public IReadOnlyCollection<TestEvent> HandledEvents => _handledEvents;
        public int ErrorCount { get; private set; }
    }

    private record TestEvent;
}