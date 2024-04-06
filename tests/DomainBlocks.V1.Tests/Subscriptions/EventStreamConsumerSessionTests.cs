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
    public async Task RestoreAsync_ReturnsPositionFromConsumerOnRestoreAsync()
    {
        var position = new SubscriptionPosition(42);
        _mockConsumer.Setup(x => x.OnRestoreAsync(CancellationToken.None)).ReturnsAsync(new SubscriptionPosition(42));

        var result = await _session.RestoreAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(position));
    }

    [Test]
    public async Task Start_StartsReadingMessages()
    {
        _session.Start();
        await _session.FlushAsync();
    }

    [Test]
    public async Task StopAsync_StopsProcessingMessages()
    {
        _session.Start();
        await _session.FlushAsync();
        await _session.StopAsync(CancellationToken.None);
        await _session.WaitForCompletedAsync();
    }
}