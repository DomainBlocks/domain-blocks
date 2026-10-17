using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.Testing;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

public class EventLogTailTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    private static readonly EventLogTailOptions Options = new()
    {
        // Long enough that only a signal wakes the tail during a test.
        PollInterval = TimeSpan.FromHours(1),
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(50)
    };

    private static readonly EventLogTailOptions SlowRetryOptions = new()
    {
        PollInterval = TimeSpan.FromHours(1),
        RetryDelay = TimeSpan.FromMinutes(1),
        MaxRetryDelay = TimeSpan.FromMinutes(1)
    };

    private static readonly ILogger Logger = new NUnitLoggerProvider().CreateLogger(nameof(EventLogTailTests));

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_EventsAppended_DeliversThemInOrderToEveryObserver(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var observer1 = new RecordingTailObserver();
        var observer2 = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(observer1, cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(observer2, cancellationToken: ct);

        eventLog.Append(3);

        (await observer1.ReadAsync(3, ct)).ShouldBe([1, 2, 3]);
        (await observer2.ReadAsync(3, ct)).ShouldBe([1, 2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_LogHasEvents_StartsAtEnd(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        eventLog.Append(2);
        await using var tail = CreateTail(eventLog);
        var observer = new RecordingTailObserver();
        await using var attachment = await tail.AttachAsync(observer, cancellationToken: ct);

        eventLog.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_TailRunning_SharesCursorSource(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);

        await using var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        eventLog.CursorSourceCreatedCount.ShouldBe(1);
        eventLog.EndPositionReadCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_LastAttachmentDisposed_StopsTail(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        var attachment2 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        await attachment1.DisposeAsync();

        eventLog.CursorSourceDisposedCount.ShouldBe(0);

        await attachment2.DisposeAsync();

        eventLog.CursorSourceDisposedCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_AttachmentDisposedTwice_DetachesOnce(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        await attachment1.DisposeAsync();
        await attachment1.DisposeAsync();

        eventLog.CursorSourceDisposedCount.ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_AfterLastDetach_StartsNewTailAtEnd(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await attachment1.DisposeAsync();
        eventLog.Append(2);

        var observer = new RecordingTailObserver();
        await using var attachment2 = await tail.AttachAsync(observer, cancellationToken: ct);
        eventLog.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([3]);
        eventLog.CursorSourceCreatedCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_TransientFailures_RetriesWithoutGapsOrDuplicates(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var observer = new RecordingTailObserver();
        await using var attachment = await tail.AttachAsync(observer, cancellationToken: ct);
        eventLog.FailNextReads(new IOException("lost"), new IOException("still lost"));

        eventLog.Append(2);
        (await observer.ReadAsync(2, ct)).ShouldBe([1, 2]);

        eventLog.Append();
        (await observer.ReadAsync(1, ct)).ShouldBe([3]);
        eventLog.CursorSourceCreatedCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NonTransientFailure_StopsTailAndNotifiesEveryObserver(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var observer1 = new RecordingTailObserver();
        var observer2 = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(observer1, cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(observer2, cancellationToken: ct);
        var exception = new InvalidOperationException("misconfigured");
        eventLog.FailNextReads(exception);

        eventLog.Append();

        (await observer1.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        (await observer2.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        eventLog.CursorSourceDisposedCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_AfterTailFailed_StartsNewTail(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var failedObserver = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(failedObserver, cancellationToken: ct);
        eventLog.FailNextReads(new InvalidOperationException("misconfigured"));
        eventLog.Append();
        await failedObserver.Error.WaitAsync(ct);

        var observer = new RecordingTailObserver();
        await using var attachment2 = await tail.AttachAsync(observer, cancellationToken: ct);
        eventLog.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([2]);
        eventLog.CursorSourceCreatedCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_StartFailsTransiently_RetriesThenAttaches(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        eventLog.FailNextEndReads(new IOException("not yet"), new IOException("still not yet"));
        await using var tail = CreateTail(eventLog);
        var observer = new RecordingTailObserver();

        await using var attachment = await tail.AttachAsync(observer, cancellationToken: ct);
        eventLog.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([1]);
        eventLog.EndPositionReadCount.ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_StartFailsNonTransiently_ThrowsAndNextAttachStartsNewTail(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        var exception = new InvalidOperationException("misconfigured");
        eventLog.FailNextEndReads(exception);
        await using var tail = CreateTail(eventLog);

        var thrown = await tail
            .AttachAsync(new RecordingTailObserver(), cancellationToken: ct)
            .ShouldThrowAsync<InvalidOperationException>();

        thrown.ShouldBeSameAs(exception);
        await using var attachment = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        eventLog.CursorSourceCreatedCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_CanceledWhileStartRetries_StopsTail(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        eventLog.FailNextEndReads(new IOException("down"));
        await using var tail = CreateTail(eventLog, SlowRetryOptions);
        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attachCts.CancelAfter(100);

        await tail
            .AttachAsync(new RecordingTailObserver(), cancellationToken: attachCts.Token)
            .ShouldThrowAsync<OperationCanceledException>();

        eventLog.CursorSourceDisposedCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_LastAttachmentWhileReadRetries_StopsTail(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog, SlowRetryOptions);
        var attachment = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        eventLog.FailNextReads(new IOException("down"));
        eventLog.Append();
        await eventLog.ReadFailure.WaitAsync(ct);

        await attachment.DisposeAsync();

        eventLog.CursorSourceDisposedCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task OnNext_ObserverThrows_DetachesOnlyThatObserver(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var tail = CreateTail(eventLog);
        var throwingObserver = new ThrowingObserver();
        var observer = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(throwingObserver, cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(observer, cancellationToken: ct);

        eventLog.Append(2);

        (await observer.ReadAsync(2, ct)).ShouldBe([1, 2]);
        throwingObserver.OnNextCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_TailRunning_StopsTailAndRejectsAttachments(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        var tail = CreateTail(eventLog);
        var attachment = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        await tail.DisposeAsync();

        eventLog.CursorSourceDisposedCount.ShouldBe(1);

        await tail
            .AttachAsync(new RecordingTailObserver(), cancellationToken: ct)
            .ShouldThrowAsync<ObjectDisposedException>();

        // Detaching after the tail is disposed is harmless.
        await attachment.DisposeAsync();
        eventLog.CursorSourceDisposedCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_WhileAttachRetriesStart_StopsTailAndRejectsAttachments(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        eventLog.FailNextEndReads(new IOException("down"));
        var tail = CreateTail(eventLog, SlowRetryOptions);
        var attachTask = tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await eventLog.ReadEndPositionFailure.WaitAsync(ct);

        await tail.DisposeAsync();

        await attachTask.ShouldThrowAsync<ObjectDisposedException>();
        eventLog.CursorSourceDisposedCount.ShouldBe(1);
    }

    private static EventLogTail<long, long> CreateTail(TestEventLog eventLog, EventLogTailOptions? options = null)
    {
        return new EventLogTail<long, long>(eventLog.CreateSource, options ?? Options, Logger);
    }

    private sealed class ThrowingObserver : ITailObserver<long>
    {
        public int OnNextCount { get; private set; }

        public void OnNext(long @event)
        {
            OnNextCount++;
            throw new InvalidOperationException("The test observer failed.");
        }

        public void OnError(Exception error)
        {
        }
    }
}