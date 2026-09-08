using DomainBlocks.EventStore.PostgreSQL.Feeds;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds;

public class RefCountedEventLogFeedTests
{
    [Test]
    public async Task Attach_FirstObserver_ConnectsFeed()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedEventLogFeed(() => feed);

        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        feed.ConnectCount.ShouldBe(1);
        feed.AttachCount.ShouldBe(1);
    }

    [Test]
    public async Task Attach_MultipleObservers_SharesConnection()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedEventLogFeed(() => feed);

        await using var attachment1 = await refCountedFeed.AttachAsync(new TestObserver());
        await using var attachment2 = await refCountedFeed.AttachAsync(new TestObserver());

        feed.ConnectCount.ShouldBe(1);
        feed.AttachCount.ShouldBe(2);
        feed.Connection!.DisposeCount.ShouldBe(0);
    }

    [Test]
    public async Task DisposeAsync_LastAttachment_DisposesConnection()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedEventLogFeed(() => feed);

        var attachment1 = await refCountedFeed.AttachAsync(new TestObserver());
        var attachment2 = await refCountedFeed.AttachAsync(new TestObserver());

        await attachment1.DisposeAsync();

        feed.Connection!.DisposeCount.ShouldBe(0);
        feed.DetachCount.ShouldBe(1);

        await attachment2.DisposeAsync();

        feed.Connection.DisposeCount.ShouldBe(1);
        feed.DetachCount.ShouldBe(2);
    }

    [Test]
    public async Task DisposeAsync_DisposeMultipleTimes_DetachesOnlyOnce()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedEventLogFeed(() => feed);
        var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        await attachment.DisposeAsync();
        await attachment.DisposeAsync();

        feed.DetachCount.ShouldBe(1);
        feed.Connection!.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task Attach_AfterConnectionFault_ReplacesConnection()
    {
        var feeds = new List<TestFeed>();

        var refCountedFeed = new RefCountedEventLogFeed(() =>
        {
            var feed = new TestFeed();
            feeds.Add(feed);
            return feed;
        });

        var attachment1 = await refCountedFeed.AttachAsync(new TestObserver());
        var attachment2 = await refCountedFeed.AttachAsync(new TestObserver());
        var connection1 = feeds[0].Connection;
        connection1!.Fault(new InvalidOperationException());

        var attachment3 = await refCountedFeed.AttachAsync(new TestObserver());

        feeds.Count.ShouldBe(2);
        feeds[0].ConnectCount.ShouldBe(1);
        feeds[0].AttachCount.ShouldBe(2);
        feeds[1].ConnectCount.ShouldBe(1);
        feeds[1].AttachCount.ShouldBe(1);

        await attachment1.DisposeAsync();
        connection1.DisposeCount.ShouldBe(0);

        await attachment2.DisposeAsync();
        connection1.DisposeCount.ShouldBe(1);

        await attachment3.DisposeAsync();
        feeds[1].Connection!.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task Attach_WhenAttachmentFails_PropagatesError()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedEventLogFeed(() => feed);
        var attachment = await refCountedFeed.AttachAsync(new TestObserver());
        var exception = new InvalidOperationException();
        feed.FaultOnNextAttach(exception);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            refCountedFeed.AttachAsync(new TestObserver()));

        thrown.ShouldBeSameAs(exception);

        feed.ConnectCount.ShouldBe(1);
        feed.AttachCount.ShouldBe(2);

        await attachment.DisposeAsync();
    }

    private sealed class TestFeed : IEventLogFeed
    {
        private Exception? _attachException;

        public int AttachCount { get; private set; }
        public int DetachCount { get; private set; }
        public int ConnectCount { get; private set; }
        public TestConnection? Connection { get; private set; }

        public IDisposable Attach(IEventLogObserver observer, string correlationId = "unknown")
        {
            AttachCount++;

            if (_attachException is { } exception)
            {
                _attachException = null;
                throw exception;
            }

            return new TestAttachment(() => DetachCount++);
        }

        public Task<IEventLogFeedConnection> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            Connection = new TestConnection();
            return Task.FromResult<IEventLogFeedConnection>(Connection);
        }

        public void FaultOnNextAttach(Exception exception) => _attachException = exception;
    }

    private sealed class TestConnection : IEventLogFeedConnection
    {
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completionTcs.Task;

        public int DisposeCount { get; private set; }

        public void Fault(Exception exception) => _completionTcs.TrySetException(exception);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _completionTcs.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAttachment(Action onDetach) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDetach();
        }
    }

    private sealed class TestObserver : IEventLogObserver
    {
        public ValueTask OnNextAsync(EventLogRow row, CancellationToken cancellationToken) => default;

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) => default;
    }
}
