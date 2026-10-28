using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class CancellationTokenSourceExtensionsTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    private static readonly TimeSpan[] BoundaryDelays =
        [TimeSpan.Zero, CancellationTokenSource.MaxSupportedDelay, Timeout.InfiniteTimeSpan];

    private static readonly TimeSpan[] OutOfRangeDelays =
        [-OneTick, Timeout.InfiniteTimeSpan - OneTick, CancellationTokenSource.MaxSupportedDelay + OneTick];

    [Test]
    public void CancelAfter_MaxSupportedDelay_DoesNotThrow()
    {
        using var cancellationTokenSource = new CancellationTokenSource();

        Should.NotThrow(() => cancellationTokenSource.CancelAfter(CancellationTokenSource.MaxSupportedDelay));
    }

    [Test]
    public void CancelAfter_LongerThanMaxSupportedDelay_Throws()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var delay = CancellationTokenSource.MaxSupportedDelay + OneTick;

        Should.Throw<ArgumentOutOfRangeException>(() => cancellationTokenSource.CancelAfter(delay));
    }

    [TestCaseSource(nameof(BoundaryDelays))]
    public void ThrowIfDelayOutOfRange_BoundaryDelay_DoesNotThrow(TimeSpan delay)
    {
        Should.NotThrow(() => CancellationTokenSource.ThrowIfDelayOutOfRange(delay));
    }

    [TestCaseSource(nameof(OutOfRangeDelays))]
    public void ThrowIfDelayOutOfRange_OutOfRangeDelay_ThrowsNamingArgument(TimeSpan delay)
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => CancellationTokenSource.ThrowIfDelayOutOfRange(delay));

        ex.ParamName.ShouldBe(nameof(delay));
    }
}