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
        var log = new TestEventLog(signalsAppends: false);
        await using var cursor = CreateCursor(log);
        await cursor.SeekToEndAsync(ct);

        log.Append();

        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_AfterFullBatch_ReadsWithoutWaiting(CancellationToken ct)
    {
        var log = new TestEventLog(signalsAppends: false);
        var options = new EventLogTailOptions { PollInterval = Options.PollInterval, MaxBatchSize = 2 };
        await using var cursor = CreateCursor(log, options);
        await cursor.SeekToEndAsync(ct);

        log.Append(3);

        (await cursor.ReadNextAsync(ct)).ShouldBe([1, 2]);
        (await cursor.ReadNextAsync(ct)).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NothingAppended_WaitsUntilAnEventIs(CancellationToken ct)
    {
        var log = new TestEventLog(signalsAppends: false);
        var options = new EventLogTailOptions { PollInterval = TimeSpan.FromMilliseconds(10) };
        await using var cursor = CreateCursor(log, options);
        await cursor.SeekToEndAsync(ct);

        // Several polls find nothing in this time, and none of them returns.
        var next = cursor.ReadNextAsync(ct).AsTask();
        await Task.Delay(100, ct);
        next.IsCompleted.ShouldBeFalse();

        log.Append();

        (await next).ShouldBe([1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_AfterShortBatch_WaitsForAppendSignal(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var cursor = CreateCursor(log);
        await cursor.SeekToEndAsync(ct);
        log.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);

        var next = cursor.ReadNextAsync(ct).AsTask();
        log.Append();

        (await next).ShouldBe([2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NoSignal_ReadsAfterPollInterval(CancellationToken ct)
    {
        var log = new TestEventLog(signalsAppends: false);
        var options = new EventLogTailOptions { PollInterval = TimeSpan.FromMilliseconds(50) };
        await using var cursor = CreateCursor(log, options);
        await cursor.SeekToEndAsync(ct);
        log.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);

        log.Append();

        (await cursor.ReadNextAsync(ct)).ShouldBe([2]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_AppendSignalledDuringRead_DoesNotWait(CancellationToken ct)
    {
        var log = new TestEventLog();
        log.DuringRead = batch =>
        {
            if (batch.Contains(2))
                log.Append();
        };

        await using var cursor = CreateCursor(log);
        await cursor.SeekToEndAsync(ct);
        log.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);
        log.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([2]);

        // Event 3 was appended, and signalled, while event 2 was being read.
        (await cursor.ReadNextAsync(ct)).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_TransientFailures_RetriesWithoutGapsOrDuplicates(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var cursor = CreateCursor(log);
        await cursor.SeekToEndAsync(ct);
        log.Append();
        (await cursor.ReadNextAsync(ct)).ShouldBe([1]);
        log.FailNextReads(new IOException("lost"), new IOException("still lost"));

        log.Append(2);

        (await cursor.ReadNextAsync(ct)).ShouldBe([2, 3]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_NonTransientFailure_Throws(CancellationToken ct)
    {
        var log = new TestEventLog();
        await using var cursor = CreateCursor(log);
        await cursor.SeekToEndAsync(ct);
        var exception = new InvalidOperationException("misconfigured");
        log.FailNextReads(exception);

        log.Append();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(cursor.ReadNextAsync(ct).AsTask());
        thrown.ShouldBeSameAs(exception);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadNextAsync_CanceledDuringRetryBackoff_Throws(CancellationToken ct)
    {
        var log = new TestEventLog();
        var options = new EventLogTailOptions
        {
            PollInterval = Options.PollInterval,
            RetryDelay = TimeSpan.FromMinutes(1),
            MaxRetryDelay = TimeSpan.FromMinutes(1)
        };

        await using var cursor = CreateCursor(log, options);
        await cursor.SeekToEndAsync(ct);
        log.FailNextReads(new IOException("down"));
        log.Append();
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(100);

        await Should.ThrowAsync<OperationCanceledException>(cursor.ReadNextAsync(readCts.Token).AsTask());
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SeekToEndAsync_TransientFailures_Retries(CancellationToken ct)
    {
        var log = new TestEventLog();
        log.Append(2);
        log.FailNextEndReads(new IOException("not yet"), new IOException("still not yet"));
        await using var cursor = CreateCursor(log);

        await cursor.SeekToEndAsync(ct);
        log.Append();

        (await cursor.ReadNextAsync(ct)).ShouldBe([3]);
        log.EndReadCount.ShouldBe(3);
    }

    [Test]
    public async Task DisposeAsync_Cursor_DisposesSource()
    {
        var log = new TestEventLog();
        var cursor = CreateCursor(log);

        await cursor.DisposeAsync();

        log.DisposedSourceCount.ShouldBe(1);
    }

    private static EventLogCursor<long, long> CreateCursor(TestEventLog log, EventLogTailOptions? options = null)
    {
        return new EventLogCursor<long, long>(log.CreateSource(), options ?? Options, logger: null, tailId: "test");
    }
}