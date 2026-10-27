using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class TimerDurationsTests
{
    private static readonly TimeSpan[] SupportedDurations = [TimeSpan.Zero, TimeSpan.FromDays(1)];

    private static readonly TimeSpan[] SupportedPeriods = [TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(1)];

    private static readonly TimeSpan[] ExtremePeriods = [TimeSpan.FromTicks(1), TimeSpan.MaxValue];

    [Test]
    public void Clamp_LongestDuration_IsAcceptedByTimers()
    {
        var duration = TimerDurations.Clamp(TimeSpan.MaxValue);

        using var cancellationTokenSource = new CancellationTokenSource();
        Should.NotThrow(() => cancellationTokenSource.CancelAfter(duration));
        Should.NotThrow(() => new Timer(_ => { }, null, duration, Timeout.InfiniteTimeSpan).Dispose());
    }

    [TestCaseSource(nameof(SupportedDurations))]
    public void Clamp_SupportedDuration_IsUnchanged(TimeSpan duration)
    {
        TimerDurations.Clamp(duration).ShouldBe(duration);
    }

    [TestCaseSource(nameof(ExtremePeriods))]
    public void ClampPeriod_ExtremePeriod_IsAcceptedByPeriodicTimer(TimeSpan period)
    {
        var clamped = TimerDurations.ClampPeriod(period);

        Should.NotThrow(() => new PeriodicTimer(clamped).Dispose());
    }

    [TestCaseSource(nameof(SupportedPeriods))]
    public void ClampPeriod_SupportedPeriod_IsUnchanged(TimeSpan period)
    {
        TimerDurations.ClampPeriod(period).ShouldBe(period);
    }
}