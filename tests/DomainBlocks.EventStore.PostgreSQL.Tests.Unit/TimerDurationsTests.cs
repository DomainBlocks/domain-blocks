using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class TimerDurationsTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    private static readonly TimeSpan[] UnsupportedPeriods =
        [TimerDurations.MinPeriod - OneTick, TimerDurations.MaxDuration + OneTick];

    private static readonly TimeSpan[] BoundaryPeriods = [TimerDurations.MinPeriod, TimerDurations.MaxDuration];

    [Test]
    public void MaxDuration_AsDuration_IsAcceptedByTimers()
    {
        var duration = TimerDurations.MaxDuration;

        using var cancellationTokenSource = new CancellationTokenSource();
        Should.NotThrow(() => cancellationTokenSource.CancelAfter(duration));
        Should.NotThrow(() => new Timer(_ => { }, null, duration, Timeout.InfiniteTimeSpan).Dispose());
    }

    [Test]
    public void MaxDuration_OneTickLonger_IsRejectedByTimers()
    {
        var duration = TimerDurations.MaxDuration + OneTick;

        using var cancellationTokenSource = new CancellationTokenSource();
        Should.Throw<ArgumentOutOfRangeException>(() => cancellationTokenSource.CancelAfter(duration));
        Should.Throw<ArgumentOutOfRangeException>(() => new Timer(_ => { }, null, duration, Timeout.InfiniteTimeSpan));
    }

    [TestCaseSource(nameof(BoundaryPeriods))]
    public void PeriodicTimer_BoundaryPeriod_IsAccepted(TimeSpan period)
    {
        Should.NotThrow(() => new PeriodicTimer(period).Dispose());
    }

    [TestCaseSource(nameof(UnsupportedPeriods))]
    public void PeriodicTimer_UnsupportedPeriod_IsRejected(TimeSpan period)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PeriodicTimer(period));
    }

    [Test]
    public void ThrowIfUnsupportedDuration_MaxDuration_DoesNotThrow()
    {
        Should.NotThrow(() => TimerDurations.ThrowIfUnsupportedDuration(TimerDurations.MaxDuration));
    }

    [Test]
    public void ThrowIfUnsupportedDuration_LongerDuration_ThrowsNamingArgument()
    {
        var delay = TimerDurations.MaxDuration + OneTick;

        var ex = Should.Throw<ArgumentOutOfRangeException>(() => TimerDurations.ThrowIfUnsupportedDuration(delay));

        ex.ParamName.ShouldBe(nameof(delay));
    }

    [TestCaseSource(nameof(BoundaryPeriods))]
    public void ThrowIfUnsupportedPeriod_BoundaryPeriod_DoesNotThrow(TimeSpan period)
    {
        Should.NotThrow(() => TimerDurations.ThrowIfUnsupportedPeriod(period));
    }

    [TestCaseSource(nameof(UnsupportedPeriods))]
    public void ThrowIfUnsupportedPeriod_UnsupportedPeriod_ThrowsNamingArgument(TimeSpan period)
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => TimerDurations.ThrowIfUnsupportedPeriod(period));

        ex.ParamName.ShouldBe(nameof(period));
    }
}