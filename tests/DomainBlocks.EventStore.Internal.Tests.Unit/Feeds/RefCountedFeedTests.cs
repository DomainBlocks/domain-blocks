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
    public async Task AttachAsync_MultipleObservers_SharesFeed()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

        await using var attachment1 = await refCountedFeed.AttachAsync(new TestObserver());
        await using var attachment2 = await refCountedFeed.AttachAsync(new TestObserver());

        feed.ConnectCount.ShouldBe(1);
        feed.AttachCount.ShouldBe(2);
        feed.DisposeCount.ShouldBe(0);
    }

    [Test]
    public async Task WaitForConnectionAsync_MarkAdvancesDuringAttach_ReturnsLaterMark()
    {
        // The feed delivers an item while the observer attaches, too late for the observer to receive it.
        var feed = new TestFeed(advanceMarkOnAttach: true);
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        (await attachment.WaitForConnectionAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_FeedConnecting_AttachesWithoutWaiting(CancellationToken ct)
    {
        var feed = new TestFeed(canConnect: false);
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

        await using var attachment1 = await refCountedFeed
            .AttachAsync(new TestObserver(), cancellationToken: ct)
            .WaitAsync(ct);

        await using var attachment2 = await refCountedFeed
            .AttachAsync(new TestObserver(), cancellationToken: ct)
            .WaitAsync(ct);

        feed.ConnectCount.ShouldBe(1);
        feed.AttachCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForConnectionAsync_FeedConnects_CompletesForEachObserver(CancellationToken ct)
    {
        var feed = new TestFeed(canConnect: false);
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        await using var attachment1 = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);
        await using var attachment2 = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);

        var waiting1 = attachment1.WaitForConnectionAsync(ct);
        var waiting2 = attachment2.WaitForConnectionAsync(ct);

        waiting1.IsCompleted.ShouldBeFalse();
        waiting2.IsCompleted.ShouldBeFalse();

        feed.AllowConnection();

        await waiting1;
        await waiting2;
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForConnectionAsync_ObserverAttachedBeforeConnection_ReturnsMarkFromConnection(
        CancellationToken ct)
    {
        var feed = new TestFeed(canConnect: false);
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);

        var waiting = attachment.WaitForConnectionAsync(ct);
        feed.AllowConnection(highWaterMark: 5);

        (await waiting).ShouldBe(5);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForConnectionAsync_Canceled_LeavesOtherObserversWaiting(CancellationToken ct)
    {
        var feed = new TestFeed(canConnect: false);
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        var attachment1 = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);
        await using var attachment2 = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var canceled = attachment1.WaitForConnectionAsync(waitCts.Token);
        var waiting = attachment2.WaitForConnectionAsync(ct);
        await waitCts.CancelAsync();

        await Task.WhenAny(canceled, Task.Delay(Timeout.Infinite, ct));
        canceled.IsCanceled.ShouldBeTrue();

        await attachment1.DisposeAsync();

        feed.DisposeCount.ShouldBe(0);
        waiting.IsCompleted.ShouldBeFalse();

        feed.AllowConnection();

        await waiting;
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForConnectionAsync_FeedFaultsBeforeConnecting_ThrowsFeedError(CancellationToken ct)
    {
        var feed = new TestFeed(canConnect: false);
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);
        var exception = new InvalidOperationException();

        var waiting = attachment.WaitForConnectionAsync(ct);
        feed.Fault(exception);

        (await Should.ThrowAsync<InvalidOperationException>(() => waiting)).ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_LastAttachmentDuringBackoff_StopsFeed(CancellationToken ct)
    {
        var options = new FeedResumeOptions { RetryDelay = TimeSpan.FromMinutes(1) };
        var resumePolicy = new FeedResumePolicy(static ex => ex is IOException, options);
        var feed = new DelegateFeed(_ => throw new IOException("down"), resumePolicy);
        var refCountedFeed = new RefCountedFeed<long, long>(() => feed);
        var attachment = await refCountedFeed.AttachAsync(new RecordingObserver(), cancellationToken: ct);

        await attachment.DisposeAsync();

        feed.DisposeCount.ShouldBe(1);
        await feed.Completion;
    }

    [Test]
    public async Task DisposeAsync_LastAttachment_DisposesFeed()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);

        var attachment1 = await refCountedFeed.AttachAsync(new TestObserver());
        var attachment2 = await refCountedFeed.AttachAsync(new TestObserver());

        await attachment1.DisposeAsync();

        feed.DisposeCount.ShouldBe(0);
        feed.DetachCount.ShouldBe(1);

        await attachment2.DisposeAsync();

        feed.DisposeCount.ShouldBe(1);
        feed.DetachCount.ShouldBe(2);
    }

    [Test]
    public async Task DisposeAsync_LastAttachmentAfterFault_DisposesFeed()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        var attachment = await refCountedFeed.AttachAsync(new TestObserver());
        feed.Fault(new InvalidOperationException());

        feed.DisposeCount.ShouldBe(0);

        await attachment.DisposeAsync();

        feed.DisposeCount.ShouldBe(1);
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
        feed.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task AttachAsync_AfterFault_ReplacesFeed()
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
        feeds[0].Fault(new InvalidOperationException());

        var attachment3 = await refCountedFeed.AttachAsync(new TestObserver());

        feeds.Count.ShouldBe(2);
        feeds[0].ConnectCount.ShouldBe(1);
        feeds[0].AttachCount.ShouldBe(2);
        feeds[1].ConnectCount.ShouldBe(1);
        feeds[1].AttachCount.ShouldBe(1);

        // Observers still attached to the faulted feed don't hold it open.
        feeds[0].DisposeCount.ShouldBe(1);

        await attachment1.DisposeAsync();
        await attachment2.DisposeAsync();
        feeds[0].DisposeCount.ShouldBe(1);

        await attachment3.DisposeAsync();
        feeds[1].DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task AttachAsync_AfterFault_DisposesFeedBeforeCreatingNext()
    {
        var feeds = new List<TestFeed>();
        var disposeCountAtNextFeed = -1;

        var refCountedFeed = new RefCountedFeed<int, long>(() =>
        {
            if (feeds.Count == 1)
                disposeCountAtNextFeed = feeds[0].DisposeCount;

            var feed = new TestFeed();
            feeds.Add(feed);
            return feed;
        });

        await using var attachment1 = await refCountedFeed.AttachAsync(new TestObserver());
        feeds[0].Fault(new InvalidOperationException());

        await using var attachment2 = await refCountedFeed.AttachAsync(new TestObserver());

        disposeCountAtNextFeed.ShouldBe(1);
        feeds[1].DisposeCount.ShouldBe(0);
    }

    [Test]
    public async Task AttachAsync_FirstAttachFails_DisposesFeed()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        feed.FaultOnNextAttach(new InvalidOperationException());

        await refCountedFeed.AttachAsync(new TestObserver()).ShouldThrowAsync<InvalidOperationException>();

        feed.DisposeCount.ShouldBe(1);
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
    public async Task AttachAsync_LastFeedStillStopping_WaitsBeforeCreatingNext()
    {
        // A feed that is stopping may still hold server resources, such as PostgreSQL's replication slot, so the next
        // feed waits until it has stopped.
        var feeds = new List<TestFeed>();

        var refCountedFeed = new RefCountedFeed<int, long>(() =>
        {
            var feed = new TestFeed();
            feeds.Add(feed);
            return feed;
        });

        var first = await refCountedFeed.AttachAsync(new TestObserver());
        feeds[0].HoldDispose();

        var detaching = first.DisposeAsync().AsTask();
        await feeds[0].Disposing;

        var attaching = refCountedFeed.AttachAsync(new TestObserver());
        feeds.Count.ShouldBe(1);

        feeds[0].ReleaseDispose();
        await detaching;
        var second = await attaching;

        feeds.Count.ShouldBe(2);
        await second.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_CanceledWhileConnecting_ConnectsAgainOnNextAttach(CancellationToken ct)
    {
        var feeds = new List<TestFeed>();

        var refCountedFeed = new RefCountedFeed<int, long>(() =>
        {
            var feed = new TestFeed(canConnect: feeds.Count > 0);
            feeds.Add(feed);
            return feed;
        });

        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var first = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);
        var waiting = first.WaitForConnectionAsync(attachCts.Token);
        await attachCts.CancelAsync();

        await waiting.ShouldThrowAsync<OperationCanceledException>();
        await first.DisposeAsync();
        feeds[0].DisposeCount.ShouldBe(1);

        await using var attachment = await refCountedFeed.AttachAsync(new TestObserver(), cancellationToken: ct);

        feeds.Count.ShouldBe(2);
        feeds[1].ConnectCount.ShouldBe(1);
        feeds[1].AttachCount.ShouldBe(1);
    }

    [Test]
    public async Task DisposeAsync_DisposesFeedAndRejectsFurtherAttachments()
    {
        var feed = new TestFeed();
        var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
        var attachment = await refCountedFeed.AttachAsync(new TestObserver());

        await refCountedFeed.DisposeAsync();

        feed.DisposeCount.ShouldBe(1);
        await Should.ThrowAsync<ObjectDisposedException>(() => refCountedFeed.AttachAsync(new TestObserver()));

        // Detaching after disposal is harmless.
        await attachment.DisposeAsync();
        feed.DisposeCount.ShouldBe(1);
    }

    // A feed that hasn't connected fails both of its tasks, and one that has connected fails only its completion.
    [TestCase(true)]
    [TestCase(false)]
    public async Task DisposeAsync_LastAttachmentAfterFault_LeavesNoUnreadError(bool hasConnected)
    {
        var error = new InvalidOperationException();

        var isReported = await IsReportedAsUnreadAsync(error, async () =>
        {
            var feed = new TestFeed(canConnect: hasConnected);
            var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
            var attachment = await refCountedFeed.AttachAsync(new TestObserver());
            feed.Fault(error);

            await attachment.DisposeAsync();
        });

        isReported.ShouldBeFalse();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AttachAsync_AfterFault_LeavesNoUnreadError(bool hasConnected)
    {
        var error = new InvalidOperationException();

        var isReported = await IsReportedAsUnreadAsync(error, async () =>
        {
            var feed = new TestFeed(canConnect: hasConnected);
            var feeds = new Queue<TestFeed>([feed, new TestFeed()]);
            var refCountedFeed = new RefCountedFeed<int, long>(feeds.Dequeue);
            await using var attachment = await refCountedFeed.AttachAsync(new TestObserver());
            feed.Fault(error);

            await using var next = await refCountedFeed.AttachAsync(new TestObserver());
        });

        isReported.ShouldBeFalse();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DisposeAsync_FeedFaulted_LeavesNoUnreadError(bool hasConnected)
    {
        var error = new InvalidOperationException();

        var isReported = await IsReportedAsUnreadAsync(error, async () =>
        {
            var feed = new TestFeed(canConnect: hasConnected);
            var refCountedFeed = new RefCountedFeed<int, long>(() => feed);
            await refCountedFeed.AttachAsync(new TestObserver());
            feed.Fault(error);

            await refCountedFeed.DisposeAsync();
        });

        isReported.ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_LastAttachmentAfterEventLogFeedFaulted_LeavesNoUnreadError(CancellationToken ct)
    {
        var error = new InvalidOperationException();

        var isReported = await IsReportedAsUnreadAsync(error, async () =>
        {
            var resumePolicy = new FeedResumePolicy(static ex => ex is IOException, new FeedResumeOptions());
            var feed = new DelegateFeed(_ => throw error, resumePolicy);
            var refCountedFeed = new RefCountedFeed<long, long>(() => feed);
            var observer = new RecordingObserver();
            var attachment = await refCountedFeed.AttachAsync(observer, cancellationToken: ct);
            await observer.Error.WaitAsync(ct);

            await attachment.DisposeAsync();
        });

        isReported.ShouldBeFalse();
    }

    // .NET reports a faulted task's unread error when the task is finalized, so the scenario must leave nothing holding
    // on to the feed. The error is compared by reference because other tests' tasks can be finalized here too.
    private static async Task<bool> IsReportedAsUnreadAsync(Exception error, Func<Task> scenario)
    {
        var isReported = false;

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerExceptions.Contains(error))
                isReported = true;
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;

        try
        {
            await scenario();

            for (var i = 0; i < 3; i++)
            {
                await Task.Delay(10);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return isReported;
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    private sealed class TestFeed(bool canConnect = true, bool advanceMarkOnAttach = false) : IEventLogFeed<int, long>
    {
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposingTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _connectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _disposeGate;
        private Exception? _attachException;

        public int AttachCount { get; private set; }
        public int DetachCount { get; private set; }
        public int ConnectCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Task Completion => _completionTcs.Task;
        public Task Disposing => _disposingTcs.Task;
        public long HighWaterMark { get; private set; }

        public IDisposable Attach(IFeedObserver<int> observer, string correlationId = "unknown")
        {
            AttachCount++;

            if (_attachException is { } exception)
            {
                _attachException = null;
                throw exception;
            }

            if (advanceMarkOnAttach)
                HighWaterMark++;

            return new TestAttachment(() => DetachCount++);
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;

            if (canConnect)
                return;

            var connection = await Task.WhenAny(_connectionTcs.Task, Completion).WaitAsync(cancellationToken);
            await connection;
        }

        public void AllowConnection(long highWaterMark = 0)
        {
            HighWaterMark = highWaterMark;
            _connectionTcs.TrySetResult();
        }

        public void FaultOnNextAttach(Exception exception) => _attachException = exception;

        public void Fault(Exception exception) => _completionTcs.TrySetException(exception);

        // Holds disposal until it is released, as a feed that is still handing out an item would.
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

        public ValueTask OnFailureAsync(FeedFailure failure, CancellationToken cancellationToken) => default;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) => default;
    }
}