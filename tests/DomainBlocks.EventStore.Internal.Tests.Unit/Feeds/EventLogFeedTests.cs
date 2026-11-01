using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.Internal.Tests.Unit.Feeds.ScriptedSession;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

public class EventLogFeedTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StabilityPeriod = TimeSpan.FromMinutes(2);

    private static readonly FeedResumeOptions FastRetryOptions = new()
    {
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(50)
    };

    private static readonly FeedResumePolicy FastResumePolicy = CreateResumePolicy(FastRetryOptions);

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionYieldsRows_NotifiesAllObservers(CancellationToken ct)
    {
        await using var feed = CreateFeed(new ScriptedSession("s1", Row(1), Row(2), Row(3)));

        var observer1 = new RecordingObserver();
        var observer2 = new RecordingObserver();
        using var attachment1 = feed.Attach(observer1);
        using var attachment2 = feed.Attach(observer2);
        await feed.ConnectAsync(ct);

        (await observer1.ReadAsync(3, ct)).ShouldBe(["row:1", "row:2", "row:3"]);
        (await observer2.ReadAsync(3, ct)).ShouldBe(["row:1", "row:2", "row:3"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenDeliveringRow_RaisesHighWaterMarkBeforeNotifying(CancellationToken ct)
    {
        await using var feed = CreateFeed(new ScriptedSession("s1", Row(7)));

        var observer = new HighWaterMarkRecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        // The observer holds the row until it knows the feed.
        observer.Feed = feed;
        observer.ReleaseOnNext();

        (await observer.HighWaterMarkOnNext.WaitAsync(ct)).ShouldBe(7);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_SessionStartsWithMark_PublishesMark(CancellationToken ct)
    {
        await using var feed = CreateFeed(new ScriptedSession("s1") { InitialMark = 3 });

        await feed.ConnectAsync(ct);

        feed.HighWaterMark.ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_SessionWithoutRows_PublishesSessionMark(CancellationToken ct)
    {
        // The session's -1 is below a long's default, so the feed must take the first session's mark as it is.
        await using var feed = CreateFeed(new ScriptedSession("s1"));

        await feed.ConnectAsync(ct);

        feed.HighWaterMark.ShouldBe(-1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_LaterSessionWithoutRows_KeepsHighWaterMark(CancellationToken ct)
    {
        await using var feed = CreateFeed(
            new ScriptedSession("s1", Row(5), Throw(new IOException("lost"))),
            new ScriptedSession("s2"));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        // The feed takes the second session's mark before it resets observers.
        (await observer.ReadAsync(2, ct)).ShouldBe(["row:5", "reset"]);
        feed.HighWaterMark.ShouldBe(5);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionFailsTransiently_ReconnectsAndResetsBeforeNewRows(CancellationToken ct)
    {
        await using var feed = CreateFeed(
            new ScriptedSession("s1", Row(1), Row(2), Throw(new IOException("lost"))),
            new ScriptedSession("s2", Row(3), Row(4)));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.ReadAsync(5, ct)).ShouldBe(["row:1", "row:2", "reset", "row:3", "row:4"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionEndsNormally_ReconnectsAndResets(CancellationToken ct)
    {
        await using var feed = CreateFeed(
            new ScriptedSession("s1", Row(1), End()),
            new ScriptedSession("s2", Row(2)));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.ReadAsync(3, ct)).ShouldBe(["row:1", "reset", "row:2"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionResumes_ReconnectsWithoutReset(CancellationToken ct)
    {
        await using var feed = CreateFeed(
            new ScriptedSession("s1", Row(1), Row(2), Throw(new IOException("lost"))),
            new ScriptedSession("s2", Row(3)) { IsResumed = true });

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.ReadAsync(3, ct)).ShouldBe(["row:1", "row:2", "row:3"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_Connected_StopsSessionBeforeDisposeAsyncCoreContinues(CancellationToken ct)
    {
        var session = new ScriptedSession("s1");
        var sessionDisposeCount = -1;

        var feed = new DelegateFeed(
            _ => Task.FromResult<IFeedSession<long, long>>(session),
            FastResumePolicy,
            onDisposing: () => sessionDisposeCount = session.DisposeCount);

        await feed.ConnectAsync(ct);
        await feed.DisposeAsync();

        sessionDisposeCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_CalledTwice_RunsDisposeAsyncCoreOnce(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1"));

        await feed.ConnectAsync(ct);
        await feed.DisposeAsync();
        await feed.DisposeAsync();

        feed.DisposeCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_NeverConnected_CompletesAndNotifiesObservers(CancellationToken ct)
    {
        var feed = CreateFeed();

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);

        await feed.DisposeAsync();

        await feed.Completion.WaitAsync(ct);
        (await observer.Error.WaitAsync(ct)).ShouldBeOfType<ObjectDisposedException>();
    }

    [Test]
    public async Task ConnectAsync_AfterDispose_Throws()
    {
        var feed = CreateFeed();

        await feed.DisposeAsync();

        await feed.ConnectAsync().ShouldThrowAsync<ObjectDisposedException>();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionFailsNonTransiently_FaultsCompletionAndNotifiesObservers(CancellationToken ct)
    {
        var exception = new InvalidOperationException("misconfigured");
        await using var feed = CreateFeed(new ScriptedSession("s1", Row(1), Throw(exception)));

        var observer1 = new RecordingObserver();
        var observer2 = new RecordingObserver();
        using var attachment1 = feed.Attach(observer1);
        using var attachment2 = feed.Attach(observer2);
        await feed.ConnectAsync(ct);

        var thrown = await feed.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();
        thrown.ShouldBeSameAs(exception);
        (await observer1.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        (await observer2.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionFailsNonTransiently_CompletesBeforeNotifying(CancellationToken ct)
    {
        await using var feed = CreateFeed(
            new ScriptedSession("s1", Row(1), Throw(new InvalidOperationException("misconfigured"))));

        var observer = new CompletionRecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        // The observer holds the row until it knows the feed.
        observer.Feed = feed;
        observer.ReleaseOnNext();

        (await observer.IsCompletedOnError.WaitAsync(ct)).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenSessionCreationFailsTransiently_RetriesThenConnects(CancellationToken ct)
    {
        var attempts = 0;

        await using var feed = new DelegateFeed(
            _ =>
            {
                attempts++;
                return attempts < 3
                    ? throw new IOException("not yet")
                    : Task.FromResult<IFeedSession<long, long>>(new ScriptedSession("s1", Row(1)));
            },
            FastResumePolicy);

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.ReadAsync(1, ct)).ShouldBe(["row:1"]);
        attempts.ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_FeedsSharePolicy_LogRetriesWithOwnFeedId(CancellationToken ct)
    {
        var logger = new FakeLogger();
        var resumePolicy = new FeedResumePolicy(static ex => ex is IOException, FastRetryOptions, logger);

        await ConnectAfterOneFailureAsync();
        await ConnectAfterOneFailureAsync();

        var startedFeedIds = FeedIds(logger, "started");

        startedFeedIds.Distinct().Count().ShouldBe(2);
        FeedIds(logger, "failed to connect").ShouldBe(startedFeedIds);

        async Task ConnectAfterOneFailureAsync()
        {
            var attempts = 0;

            await using var feed = new DelegateFeed(
                _ => ++attempts < 2
                    ? throw new IOException("not yet")
                    : Task.FromResult<IFeedSession<long, long>>(new ScriptedSession("s1")),
                resumePolicy,
                logger);

            await feed.ConnectAsync(ct);
        }

        static string?[] FeedIds(FakeLogger logger, string message) =>
        [
            .. logger.Collector
                .GetSnapshot()
                .Where(x => x.Message.Contains(message))
                .Select(x => x.GetStructuredStateValue("FeedId"))
        ];
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_TransientCancellationAtCreate_RetriesThenConnects(CancellationToken ct)
    {
        var attempts = 0;

        // A driver can report a timeout as a cancellation that the caller didn't ask for.
        await using var feed = new DelegateFeed(
            _ => ++attempts < 2
                ? throw new OperationCanceledException("Timed out.")
                : Task.FromResult<IFeedSession<long, long>>(new ScriptedSession("s1", Row(1))),
            new FeedResumePolicy(static ex => ex is OperationCanceledException, FastRetryOptions));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.ReadAsync(1, ct)).ShouldBe(["row:1"]);
        attempts.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_NoRetries_FaultsOnFirstFailure(CancellationToken ct)
    {
        var attempts = 0;
        var exception = new IOException("down");

        await using var feed = new DelegateFeed(
            _ =>
            {
                attempts++;
                throw exception;
            },
            CreateResumePolicy(new FeedResumeOptions { MaxRetryAttempts = 0 }));

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

        await using var feed = new DelegateFeed(
            _ => throw exception,
            CreateResumePolicy(new FeedResumeOptions
            {
                MaxRetryAttempts = 2,
                RetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(1)
            }));

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
        await using var feed = CreateFeed(new ScriptedSession("s1", Throw(exception)));

        await feed.ConnectAsync(ct);
        await feed.Completion.WaitAsync(ct).ShouldThrowAsync<InvalidOperationException>();

        var attachException = Should.Throw<InvalidOperationException>(() => feed.Attach(new RecordingObserver()));
        attachException.InnerException.ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_CompletesOnlyAfterSessionIsEstablished(CancellationToken ct)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var feed = new DelegateFeed(
            async _ =>
            {
                await release.Task;
                return new ScriptedSession("s1", Row(1));
            },
            FastResumePolicy);

        var connectTask = feed.ConnectAsync(ct);

        await Task.Delay(100, ct);
        connectTask.IsCompleted.ShouldBeFalse();

        release.SetResult();

        await connectTask;
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_WhenCancelledDuringRetryBackoff_StopsTheFeed(CancellationToken ct)
    {
        await using var feed = new DelegateFeed(
            _ => throw new IOException("down"),
            CreateResumePolicy(new FeedResumeOptions
            {
                RetryDelay = TimeSpan.FromMinutes(1),
                MaxRetryDelay = TimeSpan.FromMinutes(1)
            }));

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(100);

        await feed.ConnectAsync(connectCts.Token).ShouldThrowAsync<OperationCanceledException>();

        // Stopping is the feed's own doing, and what it holds across sessions waits for its owner to dispose it.
        feed.DisposeCount.ShouldBe(0);

        var exception = Should.Throw<InvalidOperationException>(() => feed.Attach(new RecordingObserver()));
        exception.Message.ShouldBe("Cannot attach to a completed feed.");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Attach_AfterConnectionIsDisposed_Throws(CancellationToken ct)
    {
        var session = new ScriptedSession("s1");
        var feed = CreateFeed(session);

        await feed.ConnectAsync(ct);
        await feed.DisposeAsync();

        session.DisposeCount.ShouldBe(1);

        var exception = Should.Throw<InvalidOperationException>(() => feed.Attach(new RecordingObserver()));
        exception.Message.ShouldBe("Cannot attach to a completed feed.");
    }

    [Test]
    public void Constructor_NonPrimitiveStructMark_Throws()
    {
        var exception = Should.Throw<TypeInitializationException>(() =>
            new UnusedFeed());

        exception.InnerException.ShouldBeOfType<NotSupportedException>();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_CalledTwice_Throws(CancellationToken ct)
    {
        await using var feed = CreateFeed(new ScriptedSession("s1"));

        await feed.ConnectAsync(ct);

        await feed.ConnectAsync(ct).ShouldThrowAsync<InvalidOperationException>();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionFailsAtOnce_DisposesSessionAndBacksOff(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var first = new ScriptedSession("s1", Throw(new IOException("lost")));
        var second = new ScriptedSession("s2", Row(1)) { IsResumed = true };
        var sessions = new Queue<IFeedSession<long, long>>([first, second]);
        await using var feed = CreateFeed(sessions, CreateClockedResumePolicy(clock));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        await observer.ReadFailureAsync(ct);
        first.DisposeCount.ShouldBe(1);

        await Task.Delay(50, ct);
        sessions.ShouldHaveSingleItem();

        (await ReadWhileAdvancingAsync(observer, clock, ct)).ShouldBe("row:1");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionFailsJustBeforeStabilityPeriod_BacksOff(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var justUnder = StabilityPeriod - TimeSpan.FromTicks(1);
        var first = new ScriptedSession("s1", Do(() => clock.Advance(justUnder)), Throw(new IOException("lost")));
        var second = new ScriptedSession("s2", Row(1)) { IsResumed = true };
        var sessions = new Queue<IFeedSession<long, long>>([first, second]);
        await using var feed = CreateFeed(sessions, CreateClockedResumePolicy(clock));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        await observer.ReadFailureAsync(ct);
        await Task.Delay(50, ct);

        sessions.ShouldHaveSingleItem();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionFailsAfterStabilityPeriod_ReconnectsWithoutDelay(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var first = new ScriptedSession("s1", Do(() => clock.Advance(StabilityPeriod)), Throw(new IOException("lost")));
        var second = new ScriptedSession("s2", Row(1)) { IsResumed = true };
        await using var feed = CreateFeed(new([first, second]), CreateClockedResumePolicy(clock));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        // The clock doesn't move again, so a delay would never end.
        (await observer.ReadAsync(1, ct)).ShouldBe(["row:1"]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionDeliversRowThenFails_BacksOff(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var first = new ScriptedSession("s1", Row(1), Throw(new IOException("lost")));
        var second = new ScriptedSession("s2", Row(2)) { IsResumed = true };
        var sessions = new Queue<IFeedSession<long, long>>([first, second]);
        await using var feed = CreateFeed(sessions, CreateClockedResumePolicy(clock));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        await observer.ReadFailureAsync(ct);
        await Task.Delay(50, ct);

        sessions.ShouldHaveSingleItem();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionEndsWithoutError_ReportsFailureAndBacksOff(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var first = new ScriptedSession("s1", End());
        var second = new ScriptedSession("s2", Row(1)) { IsResumed = true };
        var sessions = new Queue<IFeedSession<long, long>>([first, second]);
        await using var feed = CreateFeed(sessions, CreateClockedResumePolicy(clock));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.ReadFailureAsync(ct)).Error.ShouldBeOfType<FeedSessionEndedException>();

        await Task.Delay(50, ct);
        sessions.ShouldHaveSingleItem();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_FailuresBeforeStableSession_ReportsOneOutage(CancellationToken ct)
    {
        var clock = new FakeTimeProvider();
        var secondError = new IOException("lost again");
        var thirdError = new IOException("lost after a while");
        var tenSeconds = TimeSpan.FromSeconds(10);

        await using var feed = CreateFeed(
            new(
            [
                new ScriptedSession("s1", Throw(new IOException("lost"))),
                new ScriptedSession("s2", Do(() => clock.Advance(tenSeconds)), Throw(secondError)),
                new ScriptedSession("s3", Do(() => clock.Advance(StabilityPeriod)), Throw(thirdError)),
                new ScriptedSession("s4")
            ]),
            CreateClockedResumePolicy(clock));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        var first = await observer.ReadFailureAsync(ct);
        first.FailureCount.ShouldBe(1);
        first.OutageDuration.ShouldBe(TimeSpan.Zero);

        var second = await ReadFailureWhileAdvancingAsync(observer, clock, ct);
        second.Error.ShouldBeSameAs(secondError);
        second.FailureCount.ShouldBe(2);
        second.OutageDuration.ShouldBeGreaterThanOrEqualTo(tenSeconds);

        // The session stayed up for long enough, so the failure that ended it starts a new outage.
        var third = await ReadFailureWhileAdvancingAsync(observer, clock, ct);
        third.Error.ShouldBeSameAs(thirdError);
        third.FailureCount.ShouldBe(1);
        third.OutageDuration.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_FailuresAroundFirstConnection_ReportsWhetherFeedHasConnected(CancellationToken ct)
    {
        var attempts = 0;

        await using var feed = new DelegateFeed(
            _ => ++attempts < 2
                ? throw new IOException("not yet")
                : Task.FromResult<IFeedSession<long, long>>(new ScriptedSession("s1", Throw(new IOException("lost")))),
            FastResumePolicy);

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.ReadFailureAsync(ct)).HasConnected.ShouldBeFalse();
        (await observer.ReadFailureAsync(ct)).HasConnected.ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_SessionFailsNonTransiently_ReportsErrorNotFailure(CancellationToken ct)
    {
        var exception = new InvalidOperationException("misconfigured");
        await using var feed = CreateFeed(new ScriptedSession("s1", Throw(exception)));

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        (await observer.Error.WaitAsync(ct)).ShouldBeSameAs(exception);

        using var noFailure = new CancellationTokenSource(50);
        await observer.ReadFailureAsync(noFailure.Token).AsTask().ShouldThrowAsync<OperationCanceledException>();
    }

    private static async Task<string> ReadWhileAdvancingAsync(
        RecordingObserver observer,
        FakeTimeProvider clock,
        CancellationToken ct)
    {
        var reading = observer.ReadAsync(1, ct);

        while (!reading.IsCompleted)
        {
            clock.Advance(RetryDelay);
            await Task.Delay(1, ct);
        }

        return (await reading)[0];
    }

    private static async Task<FeedFailure> ReadFailureWhileAdvancingAsync(
        RecordingObserver observer,
        FakeTimeProvider clock,
        CancellationToken ct)
    {
        var reading = observer.ReadFailureAsync(ct).AsTask();

        while (!reading.IsCompleted)
        {
            clock.Advance(RetryDelay);
            await Task.Delay(1, ct);
        }

        return await reading;
    }

    private static DelegateFeed CreateFeed(params IFeedSession<long, long>[] sessions) =>
        CreateFeed(new Queue<IFeedSession<long, long>>(sessions), FastResumePolicy);

    private static DelegateFeed CreateFeed(Queue<IFeedSession<long, long>> sessions, FeedResumePolicy resumePolicy)
    {
        return new DelegateFeed(
            _ => Task.FromResult(sessions.Count > 0
                ? sessions.Dequeue()
                : throw new InvalidOperationException("The test ran out of scripted sessions.")),
            resumePolicy);
    }

    // Delays and the stability period run on the given clock, so a test decides when they pass.
    private static FeedResumePolicy CreateClockedResumePolicy(FakeTimeProvider clock)
    {
        return CreateResumePolicy(new FeedResumeOptions
        {
            RetryDelay = RetryDelay,
            MaxRetryDelay = RetryDelay,
            StabilityPeriod = StabilityPeriod,
            TimeProvider = clock
        });
    }

    private static FeedResumePolicy CreateResumePolicy(FeedResumeOptions options) =>
        new(static ex => ex is IOException, options);

    private sealed class CompletionRecordingObserver : IFeedObserver<long>
    {
        private readonly TaskCompletionSource _onNextReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _isCompletedOnError =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEventLogFeed<long, long>? Feed { get; set; }

        public Task<bool> IsCompletedOnError => _isCompletedOnError.Task;

        public void ReleaseOnNext() => _onNextReleased.TrySetResult();

        public ValueTask OnNextAsync(long position, CancellationToken cancellationToken) => new(_onNextReleased.Task);

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnFailureAsync(FeedFailure failure, CancellationToken cancellationToken) => default;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _isCompletedOnError.TrySetResult(Feed.ShouldNotBeNull().Completion.IsCompleted);
            return default;
        }
    }

    private sealed class HighWaterMarkRecordingObserver : IFeedObserver<long>
    {
        private readonly TaskCompletionSource _onNextReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<long> _highWaterMarkOnNext =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEventLogFeed<long, long>? Feed { get; set; }

        public Task<long> HighWaterMarkOnNext => _highWaterMarkOnNext.Task;

        public void ReleaseOnNext() => _onNextReleased.TrySetResult();

        public async ValueTask OnNextAsync(long position, CancellationToken cancellationToken)
        {
            await _onNextReleased.Task;
            _highWaterMarkOnNext.TrySetResult(Feed!.HighWaterMark);
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnFailureAsync(FeedFailure failure, CancellationToken cancellationToken) => default;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) => default;
    }

    // The runtime may copy a struct with two fields as two writes, so a read could see half of one.
    private readonly record struct TwoPartMark(int High, int Low) : IComparable<TwoPartMark>
    {
        public int CompareTo(TwoPartMark other) => (High, Low).CompareTo((other.High, other.Low));
    }

    private sealed class UnusedFeed() : EventLogFeed<long, TwoPartMark>(FastResumePolicy)
    {
        protected override Task<IFeedSession<long, TwoPartMark>> CreateSessionAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}