using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class CancellationTokenSourceExtensionsTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    private static readonly TimeSpan[] BoundaryDelays = [TimeSpan.Zero, CancellationTokenSource.MaxSupportedDelay];

    private static readonly TimeSpan[] OutOfRangeDelays =
        [-OneTick, CancellationTokenSource.MaxSupportedDelay + OneTick];

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
    public void ThrowIfOutOfRange_BoundaryDelay_DoesNotThrow(TimeSpan delay)
    {
        Should.NotThrow(() => CancellationTokenSource.ThrowIfOutOfRange(delay));
    }

    [TestCaseSource(nameof(OutOfRangeDelays))]
    public void ThrowIfOutOfRange_OutOfRangeDelay_ThrowsNamingArgument(TimeSpan delay)
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => CancellationTokenSource.ThrowIfOutOfRange(delay));

        ex.ParamName.ShouldBe(nameof(delay));
    }
}