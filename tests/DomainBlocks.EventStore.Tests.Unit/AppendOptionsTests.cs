using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class AppendOptionsTests
{
    private static readonly TimeSpan[] OutOfRangeTimeouts =
        [TimeSpan.Zero, TimeSpan.FromTicks(-1), Timeout.InfiniteTimeSpan - TimeSpan.FromTicks(1), TimeSpan.MinValue];

    private static readonly TimeSpan[] BoundaryTimeouts =
        [TimeSpan.FromTicks(1), TimeSpan.MaxValue, Timeout.InfiniteTimeSpan];

    [TestCaseSource(nameof(OutOfRangeTimeouts))]
    public void Timeout_OutOfRange_Throws(TimeSpan timeout)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AppendOptions { Timeout = timeout });
    }

    [TestCaseSource(nameof(BoundaryTimeouts))]
    public void Timeout_Boundary_IsSet(TimeSpan timeout)
    {
        var options = new AppendOptions { Timeout = timeout };

        options.Timeout.ShouldBe(timeout);
    }
}