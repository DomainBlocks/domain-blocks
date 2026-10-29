using DomainBlocks.EventStore.Internal.Feeds;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

public class FeedObserverHubTests
{
    [Test]
    public async Task NotifyNextAsync_TwoObservers_NotifiesBoth()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var first = new TestObserver();
        var second = new TestObserver();
        using var firstAttachment = observerHub.Attach(first, "first");
        using var secondAttachment = observerHub.Attach(second, "second");

        await observerHub.NotifyNextAsync(1, CancellationToken.None);
        await observerHub.NotifyNextAsync(2, CancellationToken.None);

        first.Received.ShouldBe(["item:1", "item:2"]);
        second.Received.ShouldBe(["item:1", "item:2"]);
    }

    [Test]
    public async Task NotifyNextAsync_ObserverThrows_DetachesObserver()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var throwing = new TestObserver(throwOnNext: true);
        var receiving = new TestObserver();
        using var throwingAttachment = observerHub.Attach(throwing, "throwing");
        using var receivingAttachment = observerHub.Attach(receiving, "receiving");

        await observerHub.NotifyNextAsync(1, CancellationToken.None);
        await observerHub.NotifyNextAsync(2, CancellationToken.None);

        throwing.Received.ShouldBe(["item:1"]);
        receiving.Received.ShouldBe(["item:1", "item:2"]);
    }

    [Test]
    public async Task NotifyResetAsync_ObserverThrows_DetachesObserver()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var throwing = new TestObserver(throwOnReset: true);
        var receiving = new TestObserver();
        using var throwingAttachment = observerHub.Attach(throwing, "throwing");
        using var receivingAttachment = observerHub.Attach(receiving, "receiving");

        await observerHub.NotifyResetAsync(CancellationToken.None);
        await observerHub.NotifyNextAsync(1, CancellationToken.None);

        throwing.Received.ShouldBe(["reset"]);
        receiving.Received.ShouldBe(["reset", "item:1"]);
    }

    [Test]
    public async Task NotifyNextAsync_Canceled_ThrowsAndKeepsObserver()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var observer = new TestObserver();
        using var attachment = observerHub.Attach(observer, "observer");

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await observerHub.NotifyNextAsync(1, new CancellationToken(canceled: true)));

        await observerHub.NotifyNextAsync(2, CancellationToken.None);

        observer.Received.ShouldBe(["item:2"]);
    }

    [Test]
    public async Task Dispose_Attachment_DetachesObserver()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var observer = new TestObserver();
        var attachment = observerHub.Attach(observer, "observer");

        attachment.Dispose();
        await observerHub.NotifyNextAsync(1, CancellationToken.None);

        observer.Received.ShouldBeEmpty();
    }

    [Test]
    public async Task NotifyErrorAsync_ObserverThrows_NotifiesRemainingObservers()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var throwing = new TestObserver(throwOnError: true);
        var receiving = new TestObserver();
        using var throwingAttachment = observerHub.Attach(throwing, "throwing");
        using var receivingAttachment = observerHub.Attach(receiving, "receiving");

        await observerHub.NotifyErrorAsync(new InvalidOperationException("failed"), CancellationToken.None);

        throwing.Received.ShouldBe(["error:failed"]);
        receiving.Received.ShouldBe(["error:failed"]);
    }

    [Test]
    public async Task NotifyErrorAsync_AfterError_DoesNotNotify()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var observer = new TestObserver();
        using var attachment = observerHub.Attach(observer, "observer");

        await observerHub.NotifyErrorAsync(new InvalidOperationException("first"), CancellationToken.None);
        await observerHub.NotifyErrorAsync(new InvalidOperationException("second"), CancellationToken.None);

        observer.Received.ShouldBe(["error:first"]);
    }

    [Test]
    public async Task NotifyErrorAsync_AfterComplete_DoesNotNotify()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var observer = new TestObserver();
        using var attachment = observerHub.Attach(observer, "observer");

        observerHub.SetComplete();
        await observerHub.NotifyErrorAsync(new InvalidOperationException("failed"), CancellationToken.None);

        observer.Received.ShouldBeEmpty();
    }

    [Test]
    public async Task Attach_AfterError_ThrowsWithError()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        var error = new InvalidOperationException("failed");
        await observerHub.NotifyErrorAsync(error, CancellationToken.None);

        var exception = Should.Throw<InvalidOperationException>(() => observerHub.Attach(new TestObserver(), "late"));

        exception.Message.ShouldBe("Cannot attach to a faulted feed.");
        exception.InnerException.ShouldBeSameAs(error);
    }

    [Test]
    public void Attach_AfterComplete_Throws()
    {
        var observerHub = new FeedObserverHub<int>(null, "feed");
        observerHub.SetComplete();

        var exception = Should.Throw<InvalidOperationException>(() => observerHub.Attach(new TestObserver(), "late"));

        exception.Message.ShouldBe("Cannot attach to a completed feed.");
    }

    private sealed class TestObserver(bool throwOnNext = false, bool throwOnReset = false, bool throwOnError = false) :
        IFeedObserver<int>
    {
        public List<string> Received { get; } = [];

        public ValueTask OnNextAsync(int item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Received.Add($"item:{item}");
            return throwOnNext ? throw new InvalidOperationException("The test observer failed.") : default;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken)
        {
            Received.Add("reset");
            return throwOnReset ? throw new InvalidOperationException("The test observer failed.") : default;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            Received.Add($"error:{exception.Message}");
            return throwOnError ? throw new InvalidOperationException("The test observer failed.") : default;
        }
    }
}