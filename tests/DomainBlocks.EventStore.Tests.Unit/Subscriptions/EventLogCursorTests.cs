using DomainBlocks.EventStore.Subscriptions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

public class EventLogCursorTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    private static readonly EventLogTailOptions Options = new()
    {
        // Long enough that only a signal ends a wait during a test.
        PollInterval = TimeSpan.FromHours(1),
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(50)
    };

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_AfterSeek_ReadsWithoutWaiting(CancellationToken ct)
    {
        var eventLog = new TestEventLog(signalsAppends: false);
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);

        eventLog.Append();

        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_AfterFullBatch_ReadsWithoutWaiting(CancellationToken ct)
    {
        var eventLog = new TestEventLog(signalsAppends: false);
        var options = new EventLogTailOptions { PollInterval = Options.PollInterval, MaxBatchSize = 2 };
        await using var cursor = CreateCursor(eventLog, options);
        await cursor.SeekToEndAsync(ct);

        eventLog.Append(3);

        (await cursor.ReadNextAsync(ct)).ShouldBe([1, 2]);
        (await cursor.ReadNextAsync(ct)).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NothingAppended_WaitsUntilAnEventIs(CancellationToken ct)
    {
        var eventLog = new TestEventLog(signalsAppends: false);
        var options = new EventLogTailOptions { PollInterval = TimeSpan.FromMilliseconds(10) };
        await using var cursor = CreateCursor(eventLog, options);
        await cursor.SeekToEndAsync(ct);

        // Several polls find nothing in this time, and none of them returns.
        var next = cursor.ReadNextAsync(ct).AsTask();
        await Task.Delay(100, ct);
        next.IsCompleted.ShouldBeFalse();

        eventLog.Append();

        (await next).ShouldBe([1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_AfterShortBatch_WaitsForAppendSignal(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);

        var next = cursor.ReadNextAsync(ct).AsTask();
        eventLog.Append();

        (await next).ShouldBe([2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NoSignal_ReadsAfterPollInterval(CancellationToken ct)
    {
        var eventLog = new TestEventLog(signalsAppends: false);
        var options = new EventLogTailOptions { PollInterval = TimeSpan.FromMilliseconds(50) };
        await using var cursor = CreateCursor(eventLog, options);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);

        eventLog.Append();

        (await cursor.ReadNextAsync(ct)).ShouldBe([2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_AppendSignalledDuringRead_DoesNotWait(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        eventLog.OnRead = batch =>
        {
            if (batch.Contains(2))
                eventLog.Append();
        };

        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);
        eventLog.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([2]);

        // Event 3 was appended, and signalled, while event 2 was being read.
        (await cursor.ReadNextAsync(ct)).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_TransientFailures_RetriesWithoutGapsOrDuplicates(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);
        eventLog.FailNextReads(new IOException("lost"), new IOException("still lost"));

        eventLog.Append(2);

        (await cursor.ReadNextAsync(ct)).ShouldBe([2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NonTransientFailure_Throws(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        var exception = new InvalidOperationException("misconfigured");
        eventLog.FailNextReads(exception);

        eventLog.Append();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(cursor.ReadNextAsync(ct).AsTask());
        thrown.ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_CanceledDuringRetryBackoff_Throws(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        var options = new EventLogTailOptions
        {
            PollInterval = Options.PollInterval,
            RetryDelay = TimeSpan.FromMinutes(1),
            MaxRetryDelay = TimeSpan.FromMinutes(1)
        };

        await using var cursor = CreateCursor(eventLog, options);
        await cursor.SeekToEndAsync(ct);
        eventLog.FailNextReads(new IOException("down"));
        eventLog.Append();
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(100);

        await Should.ThrowAsync<OperationCanceledException>(cursor.ReadNextAsync(readCts.Token).AsTask());
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SeekToEndAsync_TransientFailures_Retries(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        eventLog.Append(2);
        eventLog.FailNextEndReads(new IOException("not yet"), new IOException("still not yet"));
        await using var cursor = CreateCursor(eventLog);

        await cursor.SeekToEndAsync(ct);
        eventLog.Append();

        (await cursor.ReadNextAsync(ct)).ShouldBe([3]);
        eventLog.EndPositionReadCount.ShouldBe(3);
    }

    [Test]
    public async Task DisposeAsync_WhenInvoked_DisposesCursorSource()
    {
        var eventLog = new TestEventLog();
        var cursor = CreateCursor(eventLog);

        await cursor.DisposeAsync();

        eventLog.CursorSourceDisposedCount.ShouldBe(1);
    }

    private static EventLogCursor<long, long> CreateCursor(TestEventLog eventLog, EventLogTailOptions? options = null)
    {
        return new EventLogCursor<long, long>(
            eventLog.CreateSource(),
            options ?? Options,
            logger: null,
            tailId: "test");
    }
}