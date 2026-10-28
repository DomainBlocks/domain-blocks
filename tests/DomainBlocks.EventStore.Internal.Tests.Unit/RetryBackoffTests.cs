using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit;

public class RetryBackoffTests
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Test]
    public void TryGetNextDelay_FirstFailure_ReturnsDelayUpToFirstDelay()
    {
        var backoff = new RetryBackoff(Timeout, FirstDelay, MaxDelay, new ManualTimeProvider());

        backoff.TryGetNextDelay(out var delay).ShouldBeTrue();
        delay.ShouldBeInRange(FirstDelay / 2, FirstDelay);
    }

    [Test]
    public void TryGetNextDelay_RepeatedFailures_DoublesCeilingUpToMaxDelay()
    {
        var backoff = new RetryBackoff(Timeout, FirstDelay, MaxDelay, new ManualTimeProvider());
        TimeSpan[] ceilings = [FirstDelay, 2 * FirstDelay, 4 * FirstDelay, MaxDelay, MaxDelay];

        foreach (var ceiling in ceilings)
        {
            backoff.TryGetNextDelay(out var delay).ShouldBeTrue();
            delay.ShouldBeInRange(ceiling / 2, ceiling);
        }
    }

    [Test]
    public void TryGetNextDelay_FailingForTimeout_ReturnsFalse()
    {
        var timeProvider = new ManualTimeProvider();
        var backoff = new RetryBackoff(Timeout, FirstDelay, MaxDelay, timeProvider);

        backoff.TryGetNextDelay(out _).ShouldBeTrue();
        timeProvider.Advance(Timeout - TimeSpan.FromTicks(1));
        backoff.TryGetNextDelay(out _).ShouldBeTrue();
        timeProvider.Advance(TimeSpan.FromTicks(1));

        backoff.TryGetNextDelay(out _).ShouldBeFalse();
    }

    [Test]
    public void TryGetNextDelay_ProgressAfterTimeout_StartsNewTimeout()
    {
        var timeProvider = new ManualTimeProvider();
        var backoff = new RetryBackoff(Timeout, FirstDelay, MaxDelay, timeProvider);

        backoff.TryGetNextDelay(out _).ShouldBeTrue();
        timeProvider.Advance(2 * Timeout);
        backoff.Reset();

        backoff.TryGetNextDelay(out _).ShouldBeTrue();
    }

    [Test]
    public void TryGetNextDelay_InfiniteTimeout_KeepsReturningTrue()
    {
        var timeProvider = new ManualTimeProvider();
        var backoff = new RetryBackoff(System.Threading.Timeout.InfiniteTimeSpan, FirstDelay, MaxDelay, timeProvider);

        backoff.TryGetNextDelay(out _).ShouldBeTrue();
        timeProvider.Advance(TimeSpan.FromDays(3650));

        backoff.TryGetNextDelay(out _).ShouldBeTrue();
    }

    [Test]
    public void Reset_AfterRepeatedFailures_RestartsFromFirstDelay()
    {
        var backoff = new RetryBackoff(Timeout, FirstDelay, MaxDelay, new ManualTimeProvider());

        for (var i = 0; i < 5; i++)
            backoff.TryGetNextDelay(out _);

        backoff.Reset();

        backoff.TryGetNextDelay(out var delay).ShouldBeTrue();
        delay.ShouldBeInRange(FirstDelay / 2, FirstDelay);
    }

    // A clock that moves only when the test moves it.
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}