using DomainBlocks.EventStore.Internal.Feeds;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.Internal.Tests.Unit.Feeds.ScriptedSession;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

public class SessionFeedTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    private static readonly SessionFeedOptions FastRetryOptions = new()
    {
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(50)
    };

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionYieldsRows_NotifiesAllObservers(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1", Row(1), Row(2), Row(3)));

        var observer1 = new RecordingObserver();
        var observer2 = new RecordingObserver();
        using var attachment1 = feed.Attach(observer1);
        using var attachment2 = feed.Attach(observer2);
        await using var connection = await feed.ConnectAsync(ct);

        (await observer1.ReadAsync(3, ct)).ShouldBe(["row:1", "row:2", "row:3"]);
        (await observer2.ReadAsync(3, ct)).ShouldBe(["row:1", "row:2", "row:3"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenDeliveringRow_RaisesHighWaterMarkBeforeNotifying(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1", Row(7)));

        var observer = new HighWaterMarkRecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        // The observer holds the row until it knows the connection.
        observer.Connection = connection;
        observer.ReleaseOnNext();

        (await observer.HighWaterMarkOnNext.WaitAsync(ct)).ShouldBe(7);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_SessionStartsWithMark_PublishesMark(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1") { InitialHighWaterMark = 3 });

        await using var connection = await feed.ConnectAsync(ct);

        connection.HighWaterMark.ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_SessionWithoutRows_PublishesSessionMark(CancellationToken ct)
    {
        // The session's -1 is below a long's default, so the feed must take the first session's mark as it is.
        var feed = CreateFeed(new ScriptedSession("s1"));

        await using var connection = await feed.ConnectAsync(ct);

        connection.HighWaterMark.ShouldBe(-1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_LaterSessionWithoutRows_KeepsHighWaterMark(CancellationToken ct)
    {
        var feed = CreateFeed(
            new ScriptedSession("s1", Row(5), Throw(new IOException("lost"))),
            new ScriptedSession("s2"));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        // The feed takes the second session's mark before it resets observers.
        (await observer.ReadAsync(2, ct)).ShouldBe(["row:5", "reset"]);
        connection.HighWaterMark.ShouldBe(5);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionFailsTransiently_ReconnectsAndResetsBeforeNewRows(CancellationToken ct)
    {
        var feed = CreateFeed(
            new ScriptedSession("s1", Row(1), Row(2), Throw(new IOException("lost"))),
            new ScriptedSession("s2", Row(3), Row(4)));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        (await observer.ReadAsync(5, ct)).ShouldBe(["row:1", "row:2", "reset", "row:3", "row:4"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionEndsNormally_ReconnectsAndResets(CancellationToken ct)
    {
        var feed = CreateFeed(
            new ScriptedSession("s1", Row(1), End()),
            new ScriptedSession("s2", Row(2)));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        (await observer.ReadAsync(3, ct)).ShouldBe(["row:1", "reset", "row:2"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionResumes_ReconnectsWithoutReset(CancellationToken ct)
    {
        var feed = CreateFeed(
            new ScriptedSession("s1", Row(1), Row(2), Throw(new IOException("lost"))),
            new ScriptedSession("s2", Row(3)) { IsResumed = true });

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        (await observer.ReadAsync(3, ct)).ShouldBe(["row:1", "row:2", "row:3"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenConnectionIsDisposed_DisposesSessionFactory(CancellationToken ct)
    {
        var sessionFactory = CreateSessionFactory(new ScriptedSession("s1"));
        var feed = CreateFeed(sessionFactory);

        var connection = await feed.ConnectAsync(ct);
        sessionFactory.DisposeCount.ShouldBe(0);

        await connection.DisposeAsync();

        sessionFactory.DisposeCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionFailsNonTransiently_FaultsCompletionAndNotifiesObservers(CancellationToken ct)
    {
        var exception = new InvalidOperationException("misconfigured");
        var feed = CreateFeed(new ScriptedSession("s1", Row(1), Throw(exception)));

        var observer1 = new RecordingObserver();
        var observer2 = new RecordingObserver();
        using var attachment1 = feed.Attach(observer1);
        using var attachment2 = feed.Attach(observer2);
        await using var connection = await feed.ConnectAsync(ct);

        var thrown = await connection.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        thrown.ShouldBeSameAs(exception);
        (await observer1.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        (await observer2.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionFailsNonTransiently_CompletesBeforeNotifying(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1", Row(1), Throw(new InvalidOperationException("misconfigured"))));

        var observer = new CompletionRecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        // The observer holds the row until it knows the connection.
        observer.Connection = connection;
        observer.ReleaseOnNext();

        (await observer.IsCompletedOnError.WaitAsync(ct)).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionCreationFailsTransiently_RetriesThenConnects(CancellationToken ct)
    {
        var attempts = 0;

        var feed = new SessionFeed<long, long>(
            new DelegateSessionFactory<long>(_ =>
            {
                attempts++;
                return attempts < 3
                    ? throw new IOException("not yet")
                    : Task.FromResult<IFeedSession<long, long>>(new ScriptedSession("s1", Row(1)));
            }),
            FastRetryOptions);

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        (await observer.ReadAsync(1, ct)).ShouldBe(["row:1"]);
        attempts.ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_TransientCancellationAtCreate_RetriesThenConnects(CancellationToken ct)
    {
        var attempts = 0;

        // A driver can report a timeout as a cancellation that the caller didn't ask for.
        var feed = new SessionFeed<long, long>(
            new DelegateSessionFactory<long>(
                _ => ++attempts < 2
                    ? throw new OperationCanceledException("Timed out.")
                    : Task.FromResult<IFeedSession<long, long>>(new ScriptedSession("s1", Row(1))),
                static ex => ex is OperationCanceledException),
            FastRetryOptions);

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        (await observer.ReadAsync(1, ct)).ShouldBe(["row:1"]);
        attempts.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_NoRetries_FaultsOnFirstFailure(CancellationToken ct)
    {
        var attempts = 0;
        var exception = new IOException("down");

        var feed = new SessionFeed<long, long>(
            new DelegateSessionFactory<long>(_ =>
            {
                attempts++;
                throw exception;
            }),
            new SessionFeedOptions { MaxRetryAttempts = 0 });

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);

        await feed.ConnectAsync(ct).ShouldThrowAsync<IOException>();
        (await observer.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        attempts.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenRetriesAreExhausted_Faults(CancellationToken ct)
    {
        var exception = new IOException("still down");

        var feed = new SessionFeed<long, long>(
            new DelegateSessionFactory<long>(_ => throw exception),
            new SessionFeedOptions
            {
                MaxRetryAttempts = 2,
                RetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(1)
            });

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);

        await feed.ConnectAsync(ct).ShouldThrowAsync<IOException>();
        (await observer.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Attach_AfterNonTransientFailure_Throws(CancellationToken ct)
    {
        var exception = new InvalidOperationException("misconfigured");
        var feed = CreateFeed(new ScriptedSession("s1", Throw(exception)));

        await using var connection = await feed.ConnectAsync(ct);
        await connection.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();

        var attachException = Should.Throw<InvalidOperationException>(() => feed.Attach(new RecordingObserver()));
        attachException.InnerException.ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_CompletesOnlyAfterSessionIsEstablished(CancellationToken ct)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var feed = new SessionFeed<long, long>(
            new DelegateSessionFactory<long>(async _ =>
            {
                await release.Task;
                return new ScriptedSession("s1", Row(1));
            }),
            FastRetryOptions);

        var connectTask = feed.ConnectAsync(ct);

        await Task.Delay(100, ct);
        connectTask.IsCompleted.ShouldBeFalse();

        release.SetResult();

        await using var connection = await connectTask;
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_WhenCancelledDuringRetryBackoff_StopsTheFeed(CancellationToken ct)
    {
        var feed = new SessionFeed<long, long>(
            new DelegateSessionFactory<long>(_ => throw new IOException("down")),
            new SessionFeedOptions { RetryDelay = TimeSpan.FromMinutes(1), MaxRetryDelay = TimeSpan.FromMinutes(1) });

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(100);

        await feed.ConnectAsync(connectCts.Token).ShouldThrowAsync<OperationCanceledException>();

        var exception = Should.Throw<InvalidOperationException>(() => feed.Attach(new RecordingObserver()));
        exception.Message.ShouldBe("Cannot attach to a completed feed.");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Attach_AfterConnectionIsDisposed_Throws(CancellationToken ct)
    {
        var session = new ScriptedSession("s1");
        var feed = CreateFeed(session);

        var connection = await feed.ConnectAsync(ct);
        await connection.DisposeAsync();

        session.DisposeCount.ShouldBe(1);

        var exception = Should.Throw<InvalidOperationException>(() => feed.Attach(new RecordingObserver()));
        exception.Message.ShouldBe("Cannot attach to a completed feed.");
    }

    [Test]
    public void Constructor_NonPrimitiveStructMark_Throws()
    {
        var exception = Should.Throw<TypeInitializationException>(() =>
            new SessionFeed<long, TwoPartMark>(new UnusedSessionFactory()));

        exception.InnerException.ShouldBeOfType<NotSupportedException>();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_CalledTwice_Throws(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1"));

        await using var connection = await feed.ConnectAsync(ct);

        await feed.ConnectAsync(ct).ShouldThrowAsync<InvalidOperationException>();
    }

    private static SessionFeed<long, long> CreateFeed(params IFeedSession<long, long>[] sessions) =>
        CreateFeed(CreateSessionFactory(sessions));

    private static SessionFeed<long, long> CreateFeed(IFeedSessionFactory<long, long> sessionFactory) =>
        new(sessionFactory, FastRetryOptions);

    private static DelegateSessionFactory<long> CreateSessionFactory(params IFeedSession<long, long>[] sessions)
    {
        var queue = new Queue<IFeedSession<long, long>>(sessions);

        return new DelegateSessionFactory<long>(_ => Task.FromResult(queue.Count > 0
            ? queue.Dequeue()
            : throw new InvalidOperationException("The test ran out of scripted sessions.")));
    }

    private sealed class CompletionRecordingObserver : IFeedObserver<long>
    {
        private readonly TaskCompletionSource _onNextReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _isCompletedOnError =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IFeedConnection<long>? Connection { get; set; }

        public Task<bool> IsCompletedOnError => _isCompletedOnError.Task;

        public void ReleaseOnNext() => _onNextReleased.TrySetResult();

        public ValueTask OnNextAsync(long position, CancellationToken cancellationToken) => new(_onNextReleased.Task);

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _isCompletedOnError.TrySetResult(Connection.ShouldNotBeNull().Completion.IsCompleted);
            return default;
        }
    }

    private sealed class HighWaterMarkRecordingObserver : IFeedObserver<long>
    {
        private readonly TaskCompletionSource _onNextReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<long> _highWaterMarkOnNext =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IFeedConnection<long>? Connection { get; set; }

        public Task<long> HighWaterMarkOnNext => _highWaterMarkOnNext.Task;

        public void ReleaseOnNext() => _onNextReleased.TrySetResult();

        public async ValueTask OnNextAsync(long position, CancellationToken cancellationToken)
        {
            await _onNextReleased.Task;
            _highWaterMarkOnNext.TrySetResult(Connection!.HighWaterMark);
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) => default;
    }

    // The runtime may copy a struct with two fields as two writes, so a read could see half of one.
    private readonly record struct TwoPartMark(int High, int Low) : IComparable<TwoPartMark>
    {
        public int CompareTo(TwoPartMark other) => (High, Low).CompareTo((other.High, other.Low));
    }

    private sealed class UnusedSessionFactory : IFeedSessionFactory<long, TwoPartMark>
    {
        public Task<IFeedSession<long, TwoPartMark>> CreateAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool IsTransient(Exception exception) => false;

        public ValueTask DisposeAsync() => default;
    }
}