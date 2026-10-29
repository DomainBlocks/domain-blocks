using DomainBlocks.EventStore.Internal.Feeds;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

public class RefCountedFeedTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    [Test]
    public async Task AttachAsync_FirstObserver_ConnectsFeed()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        feed.ConnectCount.ShouldBe(1);
        feed.AttachCount.ShouldBe(1);
    }

    [Test]
    public async Task AttachAsync_MultipleObservers_SharesConnection()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

        await using var attachment1 = await refCountedFeed.AttachAsync(new TestObserver());
        await using var attachment2 = await refCountedFeed.AttachAsync(new TestObserver());

        feed.ConnectCount.ShouldBe(1);
        feed.AttachCount.ShouldBe(2);
        feed.Connection!.DisposeCount.ShouldBe(0);
    }

    [Test]
    public async Task AttachAsync_MarkAdvancesDuringAttach_CarriesLaterMark()
    {
        // The feed delivers an item while the observer attaches, too late for the observer to receive it.
        var feed = new TestFeed(advanceMarkOnAttach: true);
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        attachment.HighWaterMark.ShouldBe(1);
    }

    [Test]
    public async Task DisposeAsync_LastAttachment_DisposesConnection()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

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
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        await attachment.DisposeAsync();
        await attachment.DisposeAsync();

        feed.DetachCount.ShouldBe(1);
        feed.Connection!.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task AttachAsync_AfterConnectionFault_ReplacesConnection()
    {
        var feeds = new List<TestFeed>();

        var refCountedFeed = new RefCountedFeed<int, long>(() =>
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
    public async Task AttachAsync_WhenAttachmentFails_PropagatesError()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
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

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_WhileLastConnectionIsStillStopping_WaitsForItBeforeConnectingAgain()
    {
        // A connection that is stopping may still hold server resources, such as PostgreSQL's replication slot, so
        // the next connection waits until it has stopped.
        var feeds = new List<TestFeed>();

        var refCountedFeed = new RefCountedFeed<int, long>(() =>
        {
            var feed = new TestFeed();
            feeds.Add(feed);
            return feed;
        });

        var first = await refCountedFeed.AttachAsync(new TestObserver());
        var connection = feeds[0].Connection!;
        connection.HoldDispose();

        var detaching = first.DisposeAsync().AsTask();
        await connection.Disposing;

        var attaching = refCountedFeed.AttachAsync(new TestObserver());
        feeds.Count.ShouldBe(1);

        connection.ReleaseDispose();
        await detaching;
        var second = await attaching;

        feeds.Count.ShouldBe(2);
        await second.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_WhenCancelledWhileConnecting_ConnectsAgainOnNextAttach(CancellationToken ct)
    {
        var feeds = new List<TestFeed>();

        var refCountedFeed = new RefCountedFeed<int, long>(() =>
        {
            var feed = new TestFeed(canConnect: feeds.Count > 0);
            feeds.Add(feed);
            return feed;
        });

        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var attaching = refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: attachCts.Token);
        await attachCts.CancelAsync();

        await attaching.ShouldThrowAsync<OperationCanceledException>();

        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);

        feeds.Count.ShouldBe(2);
        feeds[0].AttachCount.ShouldBe(0);
        feeds[1].ConnectCount.ShouldBe(1);
        feeds[1].AttachCount.ShouldBe(1);
    }

    [Test]
    public async Task DisposeAsync_DisposesConnectionAndRejectsFurtherAttachments()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        await refCountedFeed.DisposeAsync();

        feed.Connection!.DisposeCount.ShouldBe(1);
        await Should.ThrowAsync<ObjectDisposedException>(() => refCountedFeed.AttachAsync(new TestObserver()));

        // Detaching after disposal is harmless.
        await attachment.DisposeAsync();
        feed.Connection.DisposeCount.ShouldBe(1);
    }

    private sealed class TestFeed(bool canConnect = true, bool advanceMarkOnAttach = false) : IFeed<int, long>
    {
        private Exception? _attachException;

        public int AttachCount { get; private set; }
        public int DetachCount { get; private set; }
        public int ConnectCount { get; private set; }
        public TestConnection? Connection { get; private set; }

        public IDisposable Attach(IFeedObserver<int> observer, string correlationId = "unknown")
        {
            AttachCount++;

            if (_attachException is { } exception)
            {
                _attachException = null;
                throw exception;
            }

            if (advanceMarkOnAttach)
                Connection!.HighWaterMark++;

            return new TestAttachment(() => DetachCount++);
        }

        public async Task<IFeedConnection<long>> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;

            if (!canConnect)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            Connection = new TestConnection();
            return Connection;
        }

        public void FaultOnNextAttach(Exception exception) => _attachException = exception;
    }

    private sealed class TestConnection : IFeedConnection<long>
    {
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposingTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _disposeGate;

        public Task Completion => _completionTcs.Task;

        public Task Disposing => _disposingTcs.Task;

        public long HighWaterMark { get; set; }

        public int DisposeCount { get; private set; }

        public void Fault(Exception exception) => _completionTcs.TrySetException(exception);

        // Holds disposal until it is released, as a connection that is still handing out an item would.
        public void HoldDispose() =>
            _disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseDispose() => _disposeGate!.SetResult();

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            _disposingTcs.TrySetResult();

            if (_disposeGate is { } gate)
                await gate.Task;

            _completionTcs.TrySetResult();
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

    private sealed class TestObserver : IFeedObserver<int>
    {
        public ValueTask OnNextAsync(int item, CancellationToken cancellationToken) => default;

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) => default;
    }
}