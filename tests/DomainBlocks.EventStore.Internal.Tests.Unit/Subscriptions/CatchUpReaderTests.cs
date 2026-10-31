using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class CatchUpReaderTests
{
    private const string SubscriptionId = "sub-1";

    [Test]
    public async Task ReadAsync_TransientError_RetriesAndLogsSubscriptionId()
    {
        var logger = new FakeLogger();
        var options = new SubscriptionOptions { CatchUpMaxRetryAttempts = 1 };
        var reader = new FailingReader(failureCount: 1, failure: null, options, logger);

        var catchUp = await reader.ReadAsync(SubscriptionOrigin.Start, 10, CancellationToken.None);

        catchUp.EndPosition.ShouldBe(LogPosition.FromInt64(7));
        reader.Attempts.ShouldBe(2);
        logger.Collector.LatestRecord.Message.ShouldContain($"[sub: {SubscriptionId}, attempt: 1]");
    }

    [Test]
    public async Task ReadAsync_TransientErrorWithoutRetries_Throws()
    {
        var options = new SubscriptionOptions { CatchUpMaxRetryAttempts = 0 };
        var reader = new FailingReader(failureCount: 1, failure: null, options, logger: null);

        await Should.ThrowAsync<IOException>(async () =>
            await reader.ReadAsync(SubscriptionOrigin.Start, 10, CancellationToken.None));

        reader.Attempts.ShouldBe(1);
    }

    [Test]
    public async Task ReadAsync_OtherError_Throws()
    {
        var options = new SubscriptionOptions { CatchUpMaxRetryAttempts = 1 };
        var reader = new FailingReader(failureCount: 1, new InvalidOperationException(), options, logger: null);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await reader.ReadAsync(SubscriptionOrigin.Start, 10, CancellationToken.None));

        reader.Attempts.ShouldBe(1);
    }

    /// <summary>
    /// Stands in for a store's reader whose first reads fail, and whose next read finds an end of 7.
    /// </summary>
    private sealed class FailingReader(
        int failureCount,
        Exception? failure,
        SubscriptionOptions options,
        ILogger? logger) :
        CatchUpReader<string, LogPosition, long>(
            static ex => ex is IOException,
            options,
            SubscriptionId,
            logger)
    {
        public int Attempts { get; private set; }

        protected override ValueTask<CatchUp<string, LogPosition>> ReadCoreAsync(
            SubscriptionOrigin<LogPosition> origin,
            long highWaterMark,
            CancellationToken cancellationToken)
        {
            Attempts++;

            if (Attempts <= failureCount)
                throw failure ?? new IOException("The read failed.");

            return ValueTask.FromResult<CatchUp<string, LogPosition>>(
                new TestCatchUp(origin, 7, ResiliencePipeline, []));
        }
    }
}