using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class CatchUpReaderTests
{
    private const string CorrelationId = "sub-1";

    [Test]
    public async Task ReadSnapshotAsync_TransientError_RetriesAndLogsSubscriptionId()
    {
        var history = new FailingHistoryReader(failureCount: 1);
        var logger = new FakeLogger();
        var reader = CreateReader(history, new SubscriptionOptions { CatchUpMaxRetryAttempts = 1 }, logger);

        var snapshot = await reader.ReadSnapshotAsync(10, SubscriptionOrigin.Start, CancellationToken.None);

        snapshot.Position.ShouldBe(LogPosition.FromInt64(7));
        history.Attempts.ShouldBe(2);
        logger.Collector.LatestRecord.Message.ShouldContain($"[sub: {CorrelationId}, attempt: 1]");
    }

    [Test]
    public async Task ReadSnapshotAsync_TransientErrorWithoutRetries_Throws()
    {
        var history = new FailingHistoryReader(failureCount: 1);
        var reader = CreateReader(history, new SubscriptionOptions { CatchUpMaxRetryAttempts = 0 });

        await Should.ThrowAsync<IOException>(async () =>
            await reader.ReadSnapshotAsync(10, SubscriptionOrigin.Start, CancellationToken.None));

        history.Attempts.ShouldBe(1);
    }

    [Test]
    public async Task ReadSnapshotAsync_OtherError_Throws()
    {
        var history = new FailingHistoryReader(failureCount: 1, new InvalidOperationException());
        var reader = CreateReader(history, new SubscriptionOptions { CatchUpMaxRetryAttempts = 1 });

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await reader.ReadSnapshotAsync(10, SubscriptionOrigin.Start, CancellationToken.None));

        history.Attempts.ShouldBe(1);
    }

    private static CatchUpReader<string, long, LogPosition> CreateReader(
        FailingHistoryReader history,
        SubscriptionOptions options,
        FakeLogger? logger = null)
    {
        return new CatchUpReader<string, long, LogPosition>(
            history,
            static ctx => ctx.LogPosition,
            options,
            static ex => ex is IOException,
            CorrelationId,
            logger);
    }

    /// <summary>
    /// Stands in for a store's reader whose first reads fail, and whose next read finds an end of 7.
    /// </summary>
    private sealed class FailingHistoryReader(int failureCount, Exception? failure = null) :
        IHistoryReader<string, long>
    {
        public int Attempts { get; private set; }

        public ValueTask<IHistorySnapshot<string>> ReadSnapshotAsync(
            long highWaterMark,
            string correlationId,
            CancellationToken cancellationToken)
        {
            Attempts++;

            if (Attempts <= failureCount)
                throw failure ?? new IOException("The read failed.");

            return ValueTask.FromResult<IHistorySnapshot<string>>(new TestHistorySnapshot(7, []));
        }
    }
}