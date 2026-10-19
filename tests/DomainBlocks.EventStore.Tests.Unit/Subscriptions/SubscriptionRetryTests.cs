using DomainBlocks.EventStore.Subscriptions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

public class SubscriptionRetryTests
{
    private const int TestTimeoutMillis = 5 * 1000;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(10);

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Pipeline_TransientFailuresWithoutProgress_GivesUpOnceTheStreakReachesTheTimeout(
        CancellationToken ct)
    {
        var clock = new ManualTimeProvider();
        var retry = CreateRetry(clock, timeout: TimeSpan.FromMinutes(1));
        var attempts = 0;

        // Each attempt fails 10 seconds after the previous one. The streak starts at the first failure, so it reaches a
        // minute at the seventh, which is not retried.
        await Should.ThrowAsync<IOException>(async () => await retry.Pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            clock.Advance(Step);
            throw new IOException("lost");
        }, ct));

        attempts.ShouldBe(7);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Pipeline_ProgressBetweenFailures_StartsANewStreak(CancellationToken ct)
    {
        var clock = new ManualTimeProvider();
        var retry = CreateRetry(clock, timeout: TimeSpan.FromMinutes(1));
        var attempts = 0;

        // The failures last 10 minutes in all, but the reader makes progress every 30 seconds.
        await retry.Pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            clock.Advance(Step);

            if (attempts % 3 == 0)
                retry.OnProgress();

            if (attempts < 60)
                throw new IOException("lost");

            return ValueTask.CompletedTask;
        }, ct);

        attempts.ShouldBe(60);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Pipeline_InfiniteTimeout_NeverGivesUp(CancellationToken ct)
    {
        var clock = new ManualTimeProvider();
        var retry = CreateRetry(clock, timeout: Timeout.InfiniteTimeSpan);
        var attempts = 0;

        await retry.Pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            clock.Advance(TimeSpan.FromHours(1));

            if (attempts < 100)
                throw new IOException("lost");

            return ValueTask.CompletedTask;
        }, ct);

        attempts.ShouldBe(100);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Pipeline_NonTransientFailure_IsNotRetried(CancellationToken ct)
    {
        var retry = CreateRetry(new ManualTimeProvider(), timeout: TimeSpan.FromMinutes(1));
        var attempts = 0;

        await Should.ThrowAsync<InvalidOperationException>(async () => await retry.Pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException("misconfigured");
        }, ct));

        attempts.ShouldBe(1);
    }

    // Retries at once, so that only the clock decides when the streak ends.
    private static SubscriptionRetry CreateRetry(TimeProvider clock, TimeSpan timeout)
    {
        var options = new EventLogTailOptions
        {
            RetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            RetryTimeout = timeout
        };

        return new SubscriptionRetry(ex => ex is IOException, options, static (_, _, _) => { }, clock);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan by) => _timestamp += by.Ticks;
    }
}