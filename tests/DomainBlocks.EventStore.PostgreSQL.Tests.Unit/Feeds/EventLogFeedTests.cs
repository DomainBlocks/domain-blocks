using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds.ScriptedSession;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds;

public class EventLogFeedTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    private static readonly EventLogFeedOptions FastRetryOptions = new()
    {
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(50)
    };

    private ILoggerFactory _loggerFactory = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerFactory = LoggerFactory.Create(x => x
            .AddProvider(new NUnitLoggerProvider())
            .SetMinimumLevel(LogLevel.Debug));

        _logger = _loggerFactory.CreateLogger<EventLogFeedTests>();
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory.Dispose();
    }

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
    public async Task Connect_WhenSessionCreationFailsTransiently_RetriesThenConnects(CancellationToken ct)
    {
        var attempts = 0;

        var feed = new EventLogFeed(
            _ =>
            {
                attempts++;
                return attempts < 3
                    ? throw new IOException("not yet")
                    : Task.FromResult<IEventLogSession>(new ScriptedSession("s1", Row(1)));
            },
            FastRetryOptions,
            _logger);

        var observer = new RecordingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        (await observer.ReadAsync(1, ct)).ShouldBe(["row:1"]);
        attempts.ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Connect_WhenRetriesAreExhausted_Faults(CancellationToken ct)
    {
        var exception = new IOException("still down");

        var feed = new EventLogFeed(
            _ => throw exception,
            new EventLogFeedOptions
            {
                MaxRetryAttempts = 2,
                RetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(1)
            },
            _logger);

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
    public async Task OnNextAsync_WhenObserverThrows_DetachesObserver(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1", Row(1), Row(2)));

        var throwingObserver = new ThrowingObserver(throwOnNext: true);
        var recordingObserver = new RecordingObserver();
        using var throwingAttachment = feed.Attach(throwingObserver);
        using var recordingAttachment = feed.Attach(recordingObserver);
        await using var connection = await feed.ConnectAsync(ct);

        (await recordingObserver.ReadAsync(2, ct)).ShouldBe(["row:1", "row:2"]);
        throwingObserver.NextCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task OnResetAsync_WhenObserverThrows_DetachesObserver(CancellationToken ct)
    {
        var feed = CreateFeed(
            new ScriptedSession("s1", End()),
            new ScriptedSession("s2", Row(1), Row(2)));

        var throwingObserver = new ThrowingObserver(throwOnReset: true);
        var recordingObserver = new RecordingObserver();
        using var throwingAttachment = feed.Attach(throwingObserver);
        using var recordingAttachment = feed.Attach(recordingObserver);
        await using var connection = await feed.ConnectAsync(ct);

        (await recordingObserver.ReadAsync(3, ct)).ShouldBe(["reset", "row:1", "row:2"]);
        throwingObserver.ResetCount.ShouldBe(1);
        throwingObserver.NextCount.ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_CompletesOnlyAfterSessionIsEstablished(CancellationToken ct)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var feed = new EventLogFeed(
            async _ =>
            {
                await release.Task;
                return new ScriptedSession("s1", Row(1));
            },
            FastRetryOptions,
            _logger);

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
        var feed = new EventLogFeed(
            _ => throw new IOException("down"),
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMinutes(1), MaxRetryDelay = TimeSpan.FromMinutes(1) },
            _logger);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(100);

        await feed.ConnectAsync(connectCts.Token).ShouldThrowAsync<OperationCanceledException>();

        var exception = Should.Throw<InvalidOperationException>(() => feed.Attach(new RecordingObserver()));
        exception.Message.ShouldBe("Cannot attach to a completed event log feed.");
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
        exception.Message.ShouldBe("Cannot attach to a completed event log feed.");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ConnectAsync_CalledTwice_Throws(CancellationToken ct)
    {
        var feed = CreateFeed(new ScriptedSession("s1"));

        await using var connection = await feed.ConnectAsync(ct);

        await feed.ConnectAsync(ct).ShouldThrowAsync<InvalidOperationException>();
    }

    private EventLogFeed CreateFeed(params IEventLogSession[] sessions)
    {
        var queue = new Queue<IEventLogSession>(sessions);

        return new EventLogFeed(
            _ => Task.FromResult(queue.Count > 0
                ? queue.Dequeue()
                : throw new InvalidOperationException("The test ran out of scripted sessions.")),
            FastRetryOptions,
            _logger);
    }

    private sealed class ThrowingObserver(bool throwOnNext = false, bool throwOnReset = false) : IEventLogObserver
    {
        public int NextCount { get; private set; }
        public int ResetCount { get; private set; }

        public ValueTask OnNextAsync(EventLogRow row, CancellationToken cancellationToken)
        {
            NextCount++;
            return throwOnNext ? throw new InvalidOperationException("The test observer failed.") : default;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken)
        {
            ResetCount++;
            return throwOnReset ? throw new InvalidOperationException("The test observer failed.") : default;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) => default;
    }
}
