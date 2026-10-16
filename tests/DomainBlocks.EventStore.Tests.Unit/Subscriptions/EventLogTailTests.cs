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
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var observer1 = new RecordingTailObserver();
        var observer2 = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(observer1, cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(observer2, cancellationToken: ct);

        log.Append(3);

        (await observer1.ReadAsync(3, ct)).ShouldBe([1, 2, 3]);
        (await observer2.ReadAsync(3, ct)).ShouldBe([1, 2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_LogHasEvents_StartsAtEnd(CancellationToken ct)
    {
        var log = new TestEventLog();
        log.Append(2);
        await using var tail = CreateTail(log);
        var observer = new RecordingTailObserver();
        await using var attachment = await tail.AttachAsync(observer, cancellationToken: ct);

        log.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_TailRunning_SharesIt(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);

        await using var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        log.SourceCount.ShouldBe(1);
        log.EndReadCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_LastAttachment_StopsTail(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        var attachment2 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        await attachment1.DisposeAsync();

        log.DisposedSourceCount.ShouldBe(0);

        await attachment2.DisposeAsync();

        log.DisposedSourceCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_AttachmentDisposedTwice_DetachesOnce(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        await attachment1.DisposeAsync();
        await attachment1.DisposeAsync();

        log.DisposedSourceCount.ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_AfterLastDetach_StartsNewTailAtEnd(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var attachment1 = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await attachment1.DisposeAsync();
        log.Append(2);

        var observer = new RecordingTailObserver();
        await using var attachment2 = await tail.AttachAsync(observer, cancellationToken: ct);
        log.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([3]);
        log.SourceCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_TransientFailures_RetriesWithoutGapsOrDuplicates(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var observer = new RecordingTailObserver();
        await using var attachment = await tail.AttachAsync(observer, cancellationToken: ct);
        log.FailNextReads(new IOException("lost"), new IOException("still lost"));

        log.Append(2);
        (await observer.ReadAsync(2, ct)).ShouldBe([1, 2]);

        log.Append();
        (await observer.ReadAsync(1, ct)).ShouldBe([3]);
        log.SourceCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NonTransientFailure_StopsTailAndNotifiesEveryObserver(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var observer1 = new RecordingTailObserver();
        var observer2 = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(observer1, cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(observer2, cancellationToken: ct);
        var exception = new InvalidOperationException("misconfigured");
        log.FailNextReads(exception);

        log.Append();

        (await observer1.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        (await observer2.Error.WaitAsync(ct)).ShouldBeSameAs(exception);
        log.DisposedSourceCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_AfterTailFailed_StartsNewTail(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var failedObserver = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(failedObserver, cancellationToken: ct);
        log.FailNextReads(new InvalidOperationException("misconfigured"));
        log.Append();
        await failedObserver.Error.WaitAsync(ct);

        var observer = new RecordingTailObserver();
        await using var attachment2 = await tail.AttachAsync(observer, cancellationToken: ct);
        log.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([2]);
        log.SourceCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_StartFailsTransiently_RetriesThenAttaches(CancellationToken ct)
    {
        var log = new TestEventLog();
        log.FailNextEndReads(new IOException("not yet"), new IOException("still not yet"));
        await using var tail = CreateTail(log);
        var observer = new RecordingTailObserver();

        await using var attachment = await tail.AttachAsync(observer, cancellationToken: ct);
        log.Append();

        (await observer.ReadAsync(1, ct)).ShouldBe([1]);
        log.EndReadCount.ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_StartFailsNonTransiently_ThrowsAndNextAttachStartsNewTail(CancellationToken ct)
    {
        var log = new TestEventLog();
        var exception = new InvalidOperationException("misconfigured");
        log.FailNextEndReads(exception);
        await using var tail = CreateTail(log);

        var thrown = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct)
            .ShouldThrowAsync<InvalidOperationException>();

        thrown.ShouldBeSameAs(exception);
        await using var attachment = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        log.SourceCount.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AttachAsync_CanceledWhileStartRetries_StopsTail(CancellationToken ct)
    {
        var log = new TestEventLog();
        log.FailNextEndReads(new IOException("down"));
        await using var tail = CreateTail(log, SlowRetryOptions);
        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attachCts.CancelAfter(100);

        await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: attachCts.Token)
            .ShouldThrowAsync<OperationCanceledException>();

        log.DisposedSourceCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_LastAttachmentWhileReadRetries_StopsTail(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log, SlowRetryOptions);
        var attachment = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        log.FailNextReads(new IOException("down"));
        log.Append();
        await log.ReadFailed.WaitAsync(ct);

        await attachment.DisposeAsync();

        log.DisposedSourceCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task OnNext_ObserverThrows_DetachesOnlyThatObserver(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var tail = CreateTail(log);
        var throwingObserver = new ThrowingObserver();
        var observer = new RecordingTailObserver();
        await using var attachment1 = await tail.AttachAsync(throwingObserver, cancellationToken: ct);
        await using var attachment2 = await tail.AttachAsync(observer, cancellationToken: ct);

        log.Append(2);

        (await observer.ReadAsync(2, ct)).ShouldBe([1, 2]);
        throwingObserver.NextCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_TailRunning_StopsItAndRejectsAttachments(CancellationToken ct)
    {
        var log = new TestEventLog();
        var tail = CreateTail(log);
        var attachment = await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);

        await tail.DisposeAsync();

        log.DisposedSourceCount.ShouldBe(1);
        await tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct)
            .ShouldThrowAsync<ObjectDisposedException>();

        // Detaching after the tail is disposed is harmless.
        await attachment.DisposeAsync();
        log.DisposedSourceCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_WhileAttachRetriesStart_StopsTailAndAttachThrows(CancellationToken ct)
    {
        var log = new TestEventLog();
        log.FailNextEndReads(new IOException("down"));
        var tail = CreateTail(log, SlowRetryOptions);
        var attachTask = tail.AttachAsync(new RecordingTailObserver(), cancellationToken: ct);
        await log.EndReadFailed.WaitAsync(ct);

        await tail.DisposeAsync();

        await attachTask.ShouldThrowAsync<ObjectDisposedException>();
        log.DisposedSourceCount.ShouldBe(1);
    }

    private static EventLogTail<long, long> CreateTail(TestEventLog log, EventLogTailOptions? options = null)
    {
        return new EventLogTail<long, long>(log.CreateSource, options ?? Options, Logger);
    }

    private sealed class ThrowingObserver : ITailObserver<long>
    {
        public int NextCount { get; private set; }

        public void OnNext(long sourceEvent)
        {
            NextCount++;
            throw new InvalidOperationException("The test observer failed.");
        }

        public void OnError(Exception error)
        {
        }
    }
}