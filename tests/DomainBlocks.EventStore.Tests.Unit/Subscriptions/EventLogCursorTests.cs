using DomainBlocks.EventStore.Subscriptions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

public class EventLogCursorTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    private static readonly EventLogTailOptions Options = new()
    {
        // Long enough that only an append that is heard ends a wait during a test.
        PollInterval = TimeSpan.FromHours(1),
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(50)
    };

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_AfterSeek_ReadsWithoutWaiting(CancellationToken ct)
    {
        var eventLog = new TestEventLog(hearsAppends: false);
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);

        eventLog.Append();

        (await ReadAsync(cursor, ct)).ShouldBe([1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_SeveralEventsAppended_ReadsThemAll(CancellationToken ct)
    {
        var eventLog = new TestEventLog(hearsAppends: false);
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);

        eventLog.Append(3);

        (await ReadAsync(cursor, ct)).ShouldBe([1, 2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForAppendAsync_AfterRead_WaitsUntilAppendIsHeard(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await ReadAsync(cursor, ct)).ShouldBe([1]);
        var readCount = eventLog.ReadCount;

        var next = WaitAndReadAsync(cursor, ct);
        await Task.Delay(100, ct);
        next.IsCompleted.ShouldBeFalse();
        eventLog.ReadCount.ShouldBe(readCount);

        eventLog.Append();

        (await next).ShouldBe([2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForAppendAsync_AppendNotHeard_EndsAfterPollInterval(CancellationToken ct)
    {
        var eventLog = new TestEventLog(hearsAppends: false);
        var options = new EventLogTailOptions { PollInterval = TimeSpan.FromMilliseconds(50) };
        await using var cursor = CreateCursor(eventLog, options);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await ReadAsync(cursor, ct)).ShouldBe([1]);

        eventLog.Append();

        (await WaitAndReadAsync(cursor, ct)).ShouldBe([2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForAppendAsync_AppendHeardDuringRead_EndsAtOnce(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        eventLog.OnRead = positions =>
        {
            if (positions.Contains(2))
                eventLog.Append();
        };

        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await ReadAsync(cursor, ct)).ShouldBe([1]);
        eventLog.Append();
        (await WaitAndReadAsync(cursor, ct)).ShouldBe([2]);

        // Event 3 was appended, and heard, while event 2 was being read.
        (await WaitAndReadAsync(cursor, ct)).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WaitForAppendAsync_AppendHeardBeforeItIsReadable_WaitsForLaterAppend(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await ReadAsync(cursor, ct)).ShouldBe([1]);

        // Event 2 is heard, but a read does not return it yet.
        eventLog.HideAppends();
        eventLog.Append();
        (await WaitAndReadAsync(cursor, ct)).ShouldBeEmpty();
        var readCount = eventLog.ReadCount;

        // The cursor read for it once, and now waits, instead of reading for it again and again.
        var next = WaitAndReadAsync(cursor, ct);
        await Task.Delay(100, ct);
        next.IsCompleted.ShouldBeFalse();
        eventLog.ReadCount.ShouldBe(readCount);

        eventLog.ShowAppends();
        eventLog.Append();

        (await next).ShouldBe([2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_TransientFailures_RetriesWithoutGapsOrDuplicates(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.Append();
        (await ReadAsync(cursor, ct)).ShouldBe([1]);
        eventLog.FailNextReads(new IOException("lost"), new IOException("still lost"));

        eventLog.Append(2);

        (await ReadAsync(cursor, ct)).ShouldBe([2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_TransientFailureMidRead_ResumesAfterLastReadEvent(CancellationToken ct)
    {
        var eventLog = new TestEventLog(hearsAppends: false);
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        eventLog.FailNextReadAfter(1, new IOException("lost"));

        eventLog.Append(3);

        (await ReadAsync(cursor, ct)).ShouldBe([1, 2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_NonTransientFailure_Throws(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        await using var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        var exception = new InvalidOperationException("misconfigured");
        eventLog.FailNextReads(exception);

        eventLog.Append();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(ReadAsync(cursor, ct));
        thrown.ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadAsync_CanceledDuringRetryBackoff_Throws(CancellationToken ct)
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

        await Should.ThrowAsync<OperationCanceledException>(ReadAsync(cursor, readCts.Token));
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

        (await ReadAsync(cursor, ct)).ShouldBe([3]);
        eventLog.EndPositionReadCount.ShouldBe(3);
    }

    [Test]
    public async Task DisposeAsync_WhenInvoked_DisposesSource()
    {
        var eventLog = new TestEventLog();
        var cursor = CreateCursor(eventLog);

        await cursor.DisposeAsync();

        eventLog.SourceDisposedCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task DisposeAsync_AfterSeek_StopsListeningBeforeDisposingSource(CancellationToken ct)
    {
        var eventLog = new TestEventLog();
        var cursor = CreateCursor(eventLog);
        await cursor.SeekToEndAsync(ct);
        await eventLog.WaitForListenerStartAsync(ct);

        await cursor.DisposeAsync();

        eventLog.ActiveListenerCount.ShouldBe(0);
        eventLog.SourceDisposedCount.ShouldBe(1);
    }

    private static EventLogCursor<TestSourceEvent, string, string, StreamPosition, LogPosition> CreateCursor(
        TestEventLog eventLog,
        EventLogTailOptions? options = null)
    {
        return new EventLogCursor<TestSourceEvent, string, string, StreamPosition, LogPosition>(
            eventLog.CreateSource(),
            options ?? Options,
            logger: null,
            tailId: "test");
    }

    // Reads once, and returns the log positions of the events that the read handed on.
    private static async Task<long[]> ReadAsync(
        EventLogCursor<TestSourceEvent, string, string, StreamPosition, LogPosition> cursor,
        CancellationToken cancellationToken)
    {
        List<long> positions = [];
        await cursor.ReadAsync(e => positions.Add((long)e.Context.LogPosition.Value), cancellationToken);

        return [.. positions];
    }

    // Waits for an append, as the tail does after each read, and then reads.
    private static async Task<long[]> WaitAndReadAsync(
        EventLogCursor<TestSourceEvent, string, string, StreamPosition, LogPosition> cursor,
        CancellationToken cancellationToken)
    {
        await cursor.WaitForAppendAsync(cancellationToken);

        return await ReadAsync(cursor, cancellationToken);
    }
}