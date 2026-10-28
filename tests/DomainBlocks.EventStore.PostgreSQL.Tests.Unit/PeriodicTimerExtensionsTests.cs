using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PeriodicTimerExtensionsTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    private static readonly TimeSpan[] BoundaryPeriods =
        [PeriodicTimer.MinSupportedPeriod, PeriodicTimer.MaxSupportedPeriod];

    private static readonly TimeSpan[] OutOfRangePeriods =
        [PeriodicTimer.MinSupportedPeriod - OneTick, PeriodicTimer.MaxSupportedPeriod + OneTick];

    [TestCaseSource(nameof(BoundaryPeriods))]
    public void Constructor_BoundaryPeriod_DoesNotThrow(TimeSpan period)
    {
        Should.NotThrow(() => new PeriodicTimer(period).Dispose());
    }

    [TestCaseSource(nameof(OutOfRangePeriods))]
    public void Constructor_OutOfRangePeriod_Throws(TimeSpan period)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PeriodicTimer(period));
    }

    [TestCaseSource(nameof(BoundaryPeriods))]
    public void ThrowIfOutOfRange_BoundaryPeriod_DoesNotThrow(TimeSpan period)
    {
        Should.NotThrow(() => PeriodicTimer.ThrowIfOutOfRange(period));
    }

    [TestCaseSource(nameof(OutOfRangePeriods))]
    public void ThrowIfOutOfRange_OutOfRangePeriod_ThrowsNamingArgument(TimeSpan period)
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => PeriodicTimer.ThrowIfOutOfRange(period));

        ex.ParamName.ShouldBe(nameof(period));
    }
}